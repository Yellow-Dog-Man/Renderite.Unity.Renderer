// SPDX-License-Identifier: MIT
#ifndef GAUSSIAN_SPLATTING_HLSL
#define GAUSSIAN_SPLATTING_HLSL

float InvSquareCentered01(float x)
{
    x -= 0.5;
    x *= 0.5;
    x = sqrt(abs(x)) * sign(x);
    return x + 0.5;
}

float3 QuatRotateVector(float3 v, float4 r)
{
    float3 t = 2 * cross(r.xyz, v);
    return v + r.w * t + cross(r.xyz, t);
}

float4 QuatMul(float4 a, float4 b)
{
    return float4(a.wwww * b + (a.xyzx * b.wwwx + a.yzxy * b.zxyy) * float4(1,1,1,-1) - a.zxyz * b.yzxz);
}

float4 QuatInverse(float4 q)
{
    return rcp(dot(q, q)) * q * float4(-1,-1,-1,1);
}

float3x3 CalcMatrixFromRotationScale(float4 rot, float3 scale)
{
    float3x3 ms = float3x3(
        scale.x, 0, 0,
        0, scale.y, 0,
        0, 0, scale.z
    );
    float x = rot.x;
    float y = rot.y;
    float z = rot.z;
    float w = rot.w;
    float3x3 mr = float3x3(
        1-2*(y*y + z*z),   2*(x*y - w*z),   2*(x*z + w*y),
          2*(x*y + w*z), 1-2*(x*x + z*z),   2*(y*z - w*x),
          2*(x*z - w*y),   2*(y*z + w*x), 1-2*(x*x + y*y)
    );
    return mul(mr, ms);
}

void CalcCovariance3D(float3x3 rotMat, out float3 sigma0, out float3 sigma1)
{
    float3x3 sig = mul(rotMat, transpose(rotMat));
    sigma0 = float3(sig._m00, sig._m01, sig._m02);
    sigma1 = float3(sig._m11, sig._m12, sig._m22);
}

// from "EWA Splatting" (Zwicker et al 2002) eq. 31
float3 CalcCovariance2D(float3 worldPos, float3 cov3d0, float3 cov3d1, float4x4 matrixV, float4x4 matrixP, float4 screenParams)
{
    float4x4 viewMatrix = matrixV;
    float3 viewPos = mul(viewMatrix, float4(worldPos, 1)).xyz;

    // this is needed in order for splats that are visible in view but clipped "quite a lot" to work
    float aspect = matrixP._m00 / matrixP._m11;
    float tanFovX = rcp(matrixP._m00);
    float tanFovY = rcp(matrixP._m11 * aspect);
    float limX = 1.3 * tanFovX;
    float limY = 1.3 * tanFovY;
    viewPos.x = clamp(viewPos.x / viewPos.z, -limX, limX) * viewPos.z;
    viewPos.y = clamp(viewPos.y / viewPos.z, -limY, limY) * viewPos.z;

    float focal = screenParams.x * matrixP._m00 / 2;

    float3x3 J = float3x3(
        focal / viewPos.z, 0, -(focal * viewPos.x) / (viewPos.z * viewPos.z),
        0, focal / viewPos.z, -(focal * viewPos.y) / (viewPos.z * viewPos.z),
        0, 0, 0
    );
    float3x3 W = (float3x3)viewMatrix;
    float3x3 T = mul(J, W);
    float3x3 V = float3x3(
        cov3d0.x, cov3d0.y, cov3d0.z,
        cov3d0.y, cov3d1.x, cov3d1.y,
        cov3d0.z, cov3d1.y, cov3d1.z
    );
    float3x3 cov = mul(T, mul(V, transpose(T)));

    // Low pass filter to make each splat at least 1px size.
    cov._m00 += 0.3;
    cov._m11 += 0.3;
    return float3(cov._m00, cov._m01, cov._m11);
}

float3 CalcConic(float3 cov2d)
{
    float det = cov2d.x * cov2d.z - cov2d.y * cov2d.y;
    return float3(cov2d.z, -cov2d.y, cov2d.x) * rcp(det);
}

float2 CalcScreenSpaceDelta(float2 svPositionXY, float2 centerXY, float4 projectionParams)
{
    float2 d = svPositionXY - centerXY;
    d.y *= projectionParams.x;
    return d;
}

float CalcPowerFromConic(float3 conic, float2 d)
{
    return -0.5 * (conic.x * d.x*d.x + conic.z * d.y*d.y) + conic.y * d.x*d.y;
}

// Morton interleaving 16x16 group i.e. by 4 bits of coordinates, based on this thread:
// https://twitter.com/rygorous/status/986715358852608000
// which is simplified version of https://fgiesen.wordpress.com/2009/12/13/decoding-morton-codes/
uint EncodeMorton2D_16x16(uint2 c)
{
    uint t = ((c.y & 0xF) << 8) | (c.x & 0xF); // ----EFGH----ABCD
    t = (t ^ (t << 2)) & 0x3333;               // --EF--GH--AB--CD
    t = (t ^ (t << 1)) & 0x5555;               // -E-F-G-H-A-B-C-D
    return (t | (t >> 7)) & 0xFF;              // --------EAFBGCHD
}
uint2 DecodeMorton2D_16x16(uint t)      // --------EAFBGCHD
{
    t = (t & 0xFF) | ((t & 0xFE) << 7); // -EAFBGCHEAFBGCHD
    t &= 0x5555;                        // -E-F-G-H-A-B-C-D
    t = (t ^ (t >> 1)) & 0x3333;        // --EF--GH--AB--CD
    t = (t ^ (t >> 2)) & 0x0f0f;        // ----EFGH----ABCD
    return uint2(t & 0xF, t >> 8);      // --------EFGHABCD
}


static const float SH_C0 = 0.2820948;
static const float SH_C1 = 0.4886025;
static const float SH_C2[] = { 1.0925484, -1.0925484, 0.3153916, -1.0925484, 0.5462742 };
static const float SH_C3[] = { -0.5900436, 2.8906114, -0.4570458, 0.3731763, -0.4570458, 1.4453057, -0.5900436 };

struct SplatSHData
{
    float3 sh0, sh1, sh2, sh3, sh4, sh5, sh6, sh7, sh8, sh9, sh10, sh11, sh12, sh13, sh14, sh15;
};

half3 ShadeSH(SplatSHData splat, half3 dir, int shOrder, bool onlySH)
{
    dir *= -1;

    half x = dir.x, y = dir.y, z = dir.z;

    // ambient band
    half3 res = splat.sh0 * SH_C0 + 0.5;
    if (onlySH)
        res = 0.5;
    // 1st degree
    if (shOrder >= 1)
    {
        res += SH_C1 * (-splat.sh1 * y + splat.sh2 * z - splat.sh3 * x);
        // 2nd degree
        if (shOrder >= 2)
        {
            half xx = x * x, yy = y * y, zz = z * z;
            half xy = x * y, yz = y * z, xz = x * z;
            res +=
                (SH_C2[0] * xy) * splat.sh4 +
                (SH_C2[1] * yz) * splat.sh5 +
                (SH_C2[2] * (2 * zz - xx - yy)) * splat.sh6 +
                (SH_C2[3] * xz) * splat.sh7 +
                (SH_C2[4] * (xx - yy)) * splat.sh8;
            // 3rd degree
            if (shOrder >= 3)
            {
                res +=
                    (SH_C3[0] * y * (3 * xx - yy)) * splat.sh9 +
                    (SH_C3[1] * xy * z) * splat.sh10 +
                    (SH_C3[2] * y * (4 * zz - xx - yy)) * splat.sh11 +
                    (SH_C3[3] * z * (2 * zz - 3 * xx - 3 * yy)) * splat.sh12 +
                    (SH_C3[4] * x * (4 * zz - xx - yy)) * splat.sh13 +
                    (SH_C3[5] * z * (xx - yy)) * splat.sh14 +
                    (SH_C3[6] * x * (xx - 3 * yy)) * splat.sh15;
            }
        }
    }
    return max(res, 0);
}

static const uint kTexWidth = 2048;

uint3 SplatIndexToPixelIndex(uint idx)
{
    uint3 res;

    uint2 xy = DecodeMorton2D_16x16(idx);
    uint width = kTexWidth / 16;
    idx >>= 8;
    res.x = (idx % width) * 16 + xy.x;
    res.y = (idx / width) * 16 + xy.y;
    res.z = 0;
    return res;
}

struct SplatChunkInfo
{
    uint colR, colG, colB, colA;
    float2 posX, posY, posZ;
    uint sclX, sclY, sclZ;
    uint shR, shG, shB;
};

static const uint CHUNK_ELEMENT_COUNT = 256;

struct SplatData
{
    float3 pos;
    int layer;
    float4 rot;
    float3 scale;
    half opacity;
    SplatSHData sh;
};

// Decode quaternion from a "smallest 3" e.g. 10.10.10.2 format
float4 DecodeRotation(float4 pq)
{
    uint idx = (uint)round(pq.w * 3.0); // note: need to round or index might come out wrong in some formats (e.g. fp16.fp16.fp16.fp16)
    float4 q;
    q.xyz = pq.xyz * sqrt(2.0) - (1.0 / sqrt(2.0));
    q.w = sqrt(1.0 - saturate(dot(q.xyz, q.xyz)));
    if (idx == 0) q = q.wxyz;
    if (idx == 1) q = q.xwyz;
    if (idx == 2) q = q.xywz;
    return q;
}
float4 PackSmallest3Rotation(float4 q)
{
    // find biggest component
    float4 absQ = abs(q);
    int index = 0;
    float maxV = absQ.x;
    if (absQ.y > maxV)
    {
        index = 1;
        maxV = absQ.y;
    }
    if (absQ.z > maxV)
    {
        index = 2;
        maxV = absQ.z;
    }
    if (absQ.w > maxV)
    {
        index = 3;
        maxV = absQ.w;
    }

    if (index == 0) q = q.yzwx;
    if (index == 1) q = q.xzwy;
    if (index == 2) q = q.xywz;

    float3 three = q.xyz * (q.w >= 0 ? 1 : -1); // -1/sqrt2..+1/sqrt2 range
    three = (three * sqrt(2.0)) * 0.5 + 0.5; // 0..1 range
    return float4(three, index / 3.0);
}

half3 DecodePacked_6_5_5(uint enc)
{
    return half3(
        (enc & 63) / 63.0,
        ((enc >> 6) & 31) / 31.0,
        ((enc >> 11) & 31) / 31.0);
}

half3 DecodePacked_5_6_5(uint enc)
{
    return half3(
        (enc & 31) / 31.0,
        ((enc >> 5) & 63) / 63.0,
        ((enc >> 11) & 31) / 31.0);
}

half3 DecodePacked_11_10_11(uint enc)
{
    return half3(
        (enc & 2047) / 2047.0,
        ((enc >> 11) & 1023) / 1023.0,
        ((enc >> 21) & 2047) / 2047.0);
}

float3 DecodePacked_16_16_16(uint2 enc)
{
    return float3(
        (enc.x & 65535) / 65535.0,
        ((enc.x >> 16) & 65535) / 65535.0,
        (enc.y & 65535) / 65535.0);
}

float4 DecodePacked_10_10_10_2(uint enc)
{
    return float4(
        (enc & 1023) / 1023.0,
        ((enc >> 10) & 1023) / 1023.0,
        ((enc >> 20) & 1023) / 1023.0,
        ((enc >> 30) & 3) / 3.0);
}
uint EncodeQuatToNorm10(float4 v) // 32 bits: 10.10.10.2
{
    return (uint) (v.x * 1023.5f) | ((uint) (v.y * 1023.5f) << 10) | ((uint) (v.z * 1023.5f) << 20) | ((uint) (v.w * 3.5f) << 30);
}

StructuredBuffer<SplatChunkInfo> _chunks;
int _SplatChunkCount;

StructuredBuffer<float4> _rawRotations;
StructuredBuffer<float> _rawOpacities;
StructuredBuffer<SplatSHData> _rawColorData;

ByteAddressBuffer _encodedPositions;
ByteAddressBuffer _encodedRotations;
ByteAddressBuffer _encodedScales;
StructuredBuffer<uint> _encodedColors;

int _shIndexesOffset;
ByteAddressBuffer _encodedSH;

Texture2D _SplatColor;

int _positionFormat;
int _rotationFormat;
int _scaleFormat;
int _colorFormat;
int _shFormat;

#define VECTOR_F32      0
#define VECTOR_NORM16   1
#define VECTOR_NORM11   2
#define VECTOR_NORM6    3

#define ROTATION_NORM10 0

#define COLOR_F32       0
#define COLOR_F16       1
#define COLOR_NORM8     2
#define COLOR_BC7       3

#define SH_F16          0
#define SH_NORM11       1
#define SH_NORM6        2
#define SH_CLUSTER64K   3
#define SH_CLUSTER32K   4
#define SH_CLUSTER16K   5
#define SH_CLUSTER8K    6
#define SH_CLUSTER4K    7

uint LoadUShort(ByteAddressBuffer dataBuffer, uint addrU)
{
    uint addrA = addrU & ~0x3;
    uint val = dataBuffer.Load(addrA);
    if (addrU != addrA)
        val >>= 16;
    return val & 0xFFFF;
}

uint LoadUInt(ByteAddressBuffer dataBuffer, uint addrU)
{
    uint addrA = addrU & ~0x3;
    uint val = dataBuffer.Load(addrA);
    if (addrU != addrA)
    {
        uint val1 = dataBuffer.Load(addrA + 4);
        val = (val >> 16) | ((val1 & 0xFFFF) << 16);
    }
    return val;
}

float3 LoadAndDecodeVector(ByteAddressBuffer dataBuffer, uint addrU, uint fmt)
{
    uint addrA = addrU & ~0x3;

    uint val0 = dataBuffer.Load(addrA);

    float3 res = 0;
    if (fmt == VECTOR_F32)
    {
        uint val1 = dataBuffer.Load(addrA + 4);
        uint val2 = dataBuffer.Load(addrA + 8);
        if (addrU != addrA)
        {
            uint val3 = dataBuffer.Load(addrA + 12);
            val0 = (val0 >> 16) | ((val1 & 0xFFFF) << 16);
            val1 = (val1 >> 16) | ((val2 & 0xFFFF) << 16);
            val2 = (val2 >> 16) | ((val3 & 0xFFFF) << 16);
        }
        res = float3(asfloat(val0), asfloat(val1), asfloat(val2));
    }
    else if (fmt == VECTOR_NORM16)
    {
        uint val1 = dataBuffer.Load(addrA + 4);
        if (addrU != addrA)
        {
            val0 = (val0 >> 16) | ((val1 & 0xFFFF) << 16);
            val1 >>= 16;
        }
        res = DecodePacked_16_16_16(uint2(val0, val1));
    }
    else if (fmt == VECTOR_NORM11)
    {
        uint val1 = dataBuffer.Load(addrA + 4);
        if (addrU != addrA)
        {
            val0 = (val0 >> 16) | ((val1 & 0xFFFF) << 16);
        }
        res = DecodePacked_11_10_11(val0);
    }
    else if (fmt == VECTOR_NORM6)
    {
        if (addrU != addrA)
            val0 >>= 16;
        res = DecodePacked_6_5_5(val0);
    }
    return res;
}

uint GetVectorStride(int vectorFormat)
{
    if (vectorFormat == VECTOR_F32)
        return 12;
    else if (vectorFormat == VECTOR_NORM16)
        return 6;
    else if (vectorFormat == VECTOR_NORM11)
        return 4;
    else if (vectorFormat == VECTOR_NORM6)
        return 2;
    else
        return 0;
}

float4 LoadSplatRotValue(uint index)
{
    if(_rotationFormat < 0)
        return _rawRotations[index];
    else
        return DecodeRotation(DecodePacked_10_10_10_2(LoadUInt(_encodedRotations, index * 4)));
}

float3 LoadSplatScaleValue(uint index)
{
    return LoadAndDecodeVector(_encodedScales, index * GetVectorStride(_scaleFormat), _scaleFormat);
}

float3 LoadSplatPosValue(uint index)
{
    return LoadAndDecodeVector(_encodedPositions, index * GetVectorStride(_positionFormat), _positionFormat);
}

half4 LoadSplatColTex(uint3 coord)
{
    half4 c = _SplatColor.Load(coord);
    
    c.rgb -= 0.5;
    c.rgb /= SH_C0;
    
    return c;
}

half4 LoadSplatColValue(uint index)
{
    if (_colorFormat == COLOR_BC7)
        return LoadSplatColTex(SplatIndexToPixelIndex(index));
    
    if (_colorFormat == COLOR_F32)
    {
        // Is there even point in having this format since we return half4?
        int idx = index * 4;
        
        return half4(
            asfloat(_encodedColors[idx + 0]),
            asfloat(_encodedColors[idx + 1]),
            asfloat(_encodedColors[idx + 2]),
            asfloat(_encodedColors[idx + 3])
        );
    }
    
    if(_colorFormat == COLOR_F16)
    {
        int idx = index * 2;
        
        uint val_xy = _encodedColors[idx + 0];
        uint val_zw = _encodedColors[idx + 1];
        
        return half4(
            f16tof32(val_xy),
            f16tof32(val_xy >> 16),
            f16tof32(val_zw),
            f16tof32(val_zw >> 16)
        );
    }
    
    if (_colorFormat == COLOR_NORM8)
    {
        uint val = _encodedColors[index];
        
        return half4(
            (val & 0xFF) / 255.0,
            ((val >> 8) & 0xFF) / 255.0,
            ((val >> 16) & 0xFF) / 255.0,
            ((val >> 24) & 0xFF) / 255.0
        );
    }
    
    return half4(0, 0, 0, 0);
}

float3 LoadSplatPos(uint idx)
{
    float3 pos = LoadSplatPosValue(idx);
    
    if (_positionFormat > VECTOR_F32)
    {
        uint chunkIdx = idx / CHUNK_ELEMENT_COUNT;
    
        if (chunkIdx < _SplatChunkCount)
        {
            SplatChunkInfo chunk = _chunks[chunkIdx];
            float3 posMin = float3(chunk.posX.x, chunk.posY.x, chunk.posZ.x);
            float3 posMax = float3(chunk.posX.y, chunk.posY.y, chunk.posZ.y);
            pos = lerp(posMin, posMax, pos);
        }
    }
    
    return pos;
}

SplatData LoadSplatData(uint idx)
{
    SplatData s = (SplatData)0;
    
    // load raw splat data, which might be chunk-relative
    s.pos = LoadSplatPosValue(idx);
    s.rot = LoadSplatRotValue(idx);
    s.scale = LoadSplatScaleValue(idx);
    
    if(_colorFormat < 0)
    {
        // The colors are part of raw SH data & opacities are loaded in a separate buffer too
        s.opacity = _rawOpacities[idx];
        s.sh = _rawColorData[idx];
    }
    else
    {
        half4 c = LoadSplatColValue(idx);
        
        s.sh.sh0 = c.rgb;
        s.opacity = c.a;
        
        // Load the rest of the SH data
        uint shStride = 0;
        
        if (_shFormat == SH_F16 || _shFormat >= SH_CLUSTER64K)
            shStride = 96; // 15*3 fp16, rounded up to multiple of 16
        else if (_shFormat == SH_NORM11)
            shStride = 60; // 15x uint
        else if (_shFormat == SH_NORM6)
            shStride = 32; // 15x ushort, rounded up to multiple of 4

        uint shIndex = idx;
        
        // For clustered SH we need to decode the index from the table
        if (_shFormat >= SH_CLUSTER64K)
            shIndex = LoadUShort(_encodedSH, _shIndexesOffset + idx * 2);

        uint shOffset = shIndex * shStride;
        uint4 shRaw0 = _encodedSH.Load4(shOffset);
        uint4 shRaw1 = _encodedSH.Load4(shOffset + 16);
        
        if (_shFormat == SH_F16 || _shFormat >= SH_CLUSTER64K)
        {
            uint4 shRaw2 = _encodedSH.Load4(shOffset + 32);
            uint4 shRaw3 = _encodedSH.Load4(shOffset + 48);
            uint4 shRaw4 = _encodedSH.Load4(shOffset + 64);
            uint3 shRaw5 = _encodedSH.Load3(shOffset + 80);
            s.sh.sh1.r = f16tof32(shRaw0.x);
            s.sh.sh1.g = f16tof32(shRaw0.x >> 16);
            s.sh.sh1.b = f16tof32(shRaw0.y);
            s.sh.sh2.r = f16tof32(shRaw0.y >> 16);
            s.sh.sh2.g = f16tof32(shRaw0.z);
            s.sh.sh2.b = f16tof32(shRaw0.z >> 16);
            s.sh.sh3.r = f16tof32(shRaw0.w);
            s.sh.sh3.g = f16tof32(shRaw0.w >> 16);
            s.sh.sh3.b = f16tof32(shRaw1.x);
            s.sh.sh4.r = f16tof32(shRaw1.x >> 16);
            s.sh.sh4.g = f16tof32(shRaw1.y);
            s.sh.sh4.b = f16tof32(shRaw1.y >> 16);
            s.sh.sh5.r = f16tof32(shRaw1.z);
            s.sh.sh5.g = f16tof32(shRaw1.z >> 16);
            s.sh.sh5.b = f16tof32(shRaw1.w);
            s.sh.sh6.r = f16tof32(shRaw1.w >> 16);
            s.sh.sh6.g = f16tof32(shRaw2.x);
            s.sh.sh6.b = f16tof32(shRaw2.x >> 16);
            s.sh.sh7.r = f16tof32(shRaw2.y);
            s.sh.sh7.g = f16tof32(shRaw2.y >> 16);
            s.sh.sh7.b = f16tof32(shRaw2.z);
            s.sh.sh8.r = f16tof32(shRaw2.z >> 16);
            s.sh.sh8.g = f16tof32(shRaw2.w);
            s.sh.sh8.b = f16tof32(shRaw2.w >> 16);
            s.sh.sh9.r = f16tof32(shRaw3.x);
            s.sh.sh9.g = f16tof32(shRaw3.x >> 16);
            s.sh.sh9.b = f16tof32(shRaw3.y);
            s.sh.sh10.r = f16tof32(shRaw3.y >> 16);
            s.sh.sh10.g = f16tof32(shRaw3.z);
            s.sh.sh10.b = f16tof32(shRaw3.z >> 16);
            s.sh.sh11.r = f16tof32(shRaw3.w);
            s.sh.sh11.g = f16tof32(shRaw3.w >> 16);
            s.sh.sh11.b = f16tof32(shRaw4.x);
            s.sh.sh12.r = f16tof32(shRaw4.x >> 16);
            s.sh.sh12.g = f16tof32(shRaw4.y);
            s.sh.sh12.b = f16tof32(shRaw4.y >> 16);
            s.sh.sh13.r = f16tof32(shRaw4.z);
            s.sh.sh13.g = f16tof32(shRaw4.z >> 16);
            s.sh.sh13.b = f16tof32(shRaw4.w);
            s.sh.sh14.r = f16tof32(shRaw4.w >> 16);
            s.sh.sh14.g = f16tof32(shRaw5.x);
            s.sh.sh14.b = f16tof32(shRaw5.x >> 16);
            s.sh.sh15.r = f16tof32(shRaw5.y);
            s.sh.sh15.g = f16tof32(shRaw5.y >> 16);
            s.sh.sh15.b = f16tof32(shRaw5.z);
        }
        else if (_shFormat == SH_NORM11)
        {
            uint4 shRaw2 = _encodedSH.Load4(shOffset + 32);
            uint3 shRaw3 = _encodedSH.Load3(shOffset + 48);
            s.sh.sh1 = DecodePacked_11_10_11(shRaw0.x);
            s.sh.sh2 = DecodePacked_11_10_11(shRaw0.y);
            s.sh.sh3 = DecodePacked_11_10_11(shRaw0.z);
            s.sh.sh4 = DecodePacked_11_10_11(shRaw0.w);
            s.sh.sh5 = DecodePacked_11_10_11(shRaw1.x);
            s.sh.sh6 = DecodePacked_11_10_11(shRaw1.y);
            s.sh.sh7 = DecodePacked_11_10_11(shRaw1.z);
            s.sh.sh8 = DecodePacked_11_10_11(shRaw1.w);
            s.sh.sh9 = DecodePacked_11_10_11(shRaw2.x);
            s.sh.sh10 = DecodePacked_11_10_11(shRaw2.y);
            s.sh.sh11 = DecodePacked_11_10_11(shRaw2.z);
            s.sh.sh12 = DecodePacked_11_10_11(shRaw2.w);
            s.sh.sh13 = DecodePacked_11_10_11(shRaw3.x);
            s.sh.sh14 = DecodePacked_11_10_11(shRaw3.y);
            s.sh.sh15 = DecodePacked_11_10_11(shRaw3.z);
        }
        else if (_shFormat == SH_NORM6)
        {
            s.sh.sh1 = DecodePacked_5_6_5(shRaw0.x);
            s.sh.sh2 = DecodePacked_5_6_5(shRaw0.x >> 16);
            s.sh.sh3 = DecodePacked_5_6_5(shRaw0.y);
            s.sh.sh4 = DecodePacked_5_6_5(shRaw0.y >> 16);
            s.sh.sh5 = DecodePacked_5_6_5(shRaw0.z);
            s.sh.sh6 = DecodePacked_5_6_5(shRaw0.z >> 16);
            s.sh.sh7 = DecodePacked_5_6_5(shRaw0.w);
            s.sh.sh8 = DecodePacked_5_6_5(shRaw0.w >> 16);
            s.sh.sh9 = DecodePacked_5_6_5(shRaw1.x);
            s.sh.sh10 = DecodePacked_5_6_5(shRaw1.x >> 16);
            s.sh.sh11 = DecodePacked_5_6_5(shRaw1.y);
            s.sh.sh12 = DecodePacked_5_6_5(shRaw1.y >> 16);
            s.sh.sh13 = DecodePacked_5_6_5(shRaw1.z);
            s.sh.sh14 = DecodePacked_5_6_5(shRaw1.z >> 16);
            s.sh.sh15 = DecodePacked_5_6_5(shRaw1.w);
        }
    }

    // if raw data is chunk-relative, convert to final values by interpolating between chunk min/max
    uint chunkIdx = idx / CHUNK_ELEMENT_COUNT;
    
    if (chunkIdx < _SplatChunkCount)
    {
        SplatChunkInfo chunk = _chunks[chunkIdx];
        
        float3 posMin = float3(chunk.posX.x, chunk.posY.x, chunk.posZ.x);
        float3 posMax = float3(chunk.posX.y, chunk.posY.y, chunk.posZ.y);
        
        half3 sclMin = half3(f16tof32(chunk.sclX    ), f16tof32(chunk.sclY    ), f16tof32(chunk.sclZ    ));
        half3 sclMax = half3(f16tof32(chunk.sclX>>16), f16tof32(chunk.sclY>>16), f16tof32(chunk.sclZ>>16));
        
        half4 colMin = half4(f16tof32(chunk.colR    ), f16tof32(chunk.colG    ), f16tof32(chunk.colB    ), f16tof32(chunk.colA    ));
        half4 colMax = half4(f16tof32(chunk.colR>>16), f16tof32(chunk.colG>>16), f16tof32(chunk.colB>>16), f16tof32(chunk.colA>>16));
        
        half3 shMin = half3(f16tof32(chunk.shR    ), f16tof32(chunk.shG    ), f16tof32(chunk.shB    ));
        half3 shMax = half3(f16tof32(chunk.shR>>16), f16tof32(chunk.shG>>16), f16tof32(chunk.shB>>16));
        
        if (_positionFormat > VECTOR_F32)
            s.pos = lerp(posMin, posMax, s.pos);
        
        if (_scaleFormat > VECTOR_F32)
        {
            s.scale = lerp(sclMin, sclMax, s.scale);
            s.scale *= s.scale;
            s.scale *= s.scale;
            s.scale *= s.scale;
        }
        
        if (_colorFormat == COLOR_NORM8)
        {
            half4 col = half4(s.sh.sh0, s.opacity);
            
            col = lerp(colMin, colMax, col);
            col.a = InvSquareCentered01(col.a);
            
            s.sh.sh0 = col;
            s.opacity = col.a;
        }

        if (_shFormat >= SH_NORM11 && _shFormat <= SH_NORM6)
        {
            s.sh.sh1    = lerp(shMin, shMax, s.sh.sh1 );
            s.sh.sh2    = lerp(shMin, shMax, s.sh.sh2 );
            s.sh.sh3    = lerp(shMin, shMax, s.sh.sh3 );
            s.sh.sh4    = lerp(shMin, shMax, s.sh.sh4 );
            s.sh.sh5    = lerp(shMin, shMax, s.sh.sh5 );
            s.sh.sh6    = lerp(shMin, shMax, s.sh.sh6 );
            s.sh.sh7    = lerp(shMin, shMax, s.sh.sh7 );
            s.sh.sh8    = lerp(shMin, shMax, s.sh.sh8 );
            s.sh.sh9    = lerp(shMin, shMax, s.sh.sh9 );
            s.sh.sh10   = lerp(shMin, shMax, s.sh.sh10);
            s.sh.sh11   = lerp(shMin, shMax, s.sh.sh11);
            s.sh.sh12   = lerp(shMin, shMax, s.sh.sh12);
            s.sh.sh13   = lerp(shMin, shMax, s.sh.sh13);
            s.sh.sh14   = lerp(shMin, shMax, s.sh.sh14);
            s.sh.sh15   = lerp(shMin, shMax, s.sh.sh15);
        }
    }

    return s;
}

struct SplatViewData
{
    float4 pos;
    float2 axis1, axis2;
    uint2 color; // 4xFP16
};

#endif // GAUSSIAN_SPLATTING_HLSL
