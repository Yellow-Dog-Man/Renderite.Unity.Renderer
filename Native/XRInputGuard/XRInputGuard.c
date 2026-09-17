/*
 * XRInputGuard: stops a Unity crash caused by stale pointers to freed VR devices (for example swapping between controllers and hand tracking).
 * Unity's native input code keeps dereferencing a device table entry after the device is gone, which would normally end the player with an access violation.
 * This DLL registers a Windows vectored exception handler (VEH) when it loads, so every access violation passes through it first.
 * If the fault address is one of the hardcoded UnityPlayer sites below (checked by byte signature at load and again on every fault), the handler repairs the CPU context and resumes the thread: feature lookups get a zeroed buffer (one frame of neutral input), the indexed feature lookup gets a zeroed index register (its own clamp treats that as invalid), and copy helpers are skipped (destination keeps its last good data).
 * If anything looks unfamiliar like unknown Unity build and signature mismatch, the guard disarms itself loudly through the status string instead of hiding corruption, and the status names the exact site that needs updating.
 * GetFeature-B is repaired with a buffer address rather than zero on purpose: callers treat huge values as invalid, while zero could silently read the first table entry.
 * This works around a Unity 2019.4 bug that is likely fixed in newer Unity versions, patching the fault is the only option until the engine is upgraded.
 */

#include <string.h>
#include <stdio.h>
#include <windows.h>

typedef unsigned long long Addr;

#define MAX_SITES 24
#define STATUS_LEN 256
#define MAX_SCAN_AHEAD 40

typedef enum { FIX_SKIP = 0, FIX_ZERO_PTR = 1, FIX_ZERO_RDI = 2 } FixKind;

typedef struct
{
    Addr rva;
    unsigned skip;
    FixKind fix;
} GuardSite;

typedef struct
{
    Addr rva;
    FixKind fix;
    const char *name;
    unsigned char sig[8];
    int siglen;
} SiteDef;

// Guarded fault sites: { UnityPlayer RVA, repair, name, expected bytes, byte count }. Each row is verified at load and re-verified on every fault, any mismatch disarms the whole guard.
// Feature lookups return a pointer into a possibly-freed device table, so a fault here is repaired by returning a zeroed buffer instead.
static const SiteDef kSites[] =
{
    { 0x0B2068Aull, FIX_ZERO_PTR, "GetFeature-A", { 0x8B, 0x04, 0x90, 0x48, 0x03, 0x41, 0x28, 0xC3 }, 8 },
    { 0x0B2066Aull, FIX_ZERO_PTR, "GetFeature-B", { 0x8B, 0x04, 0x90, 0xC3 }, 4 },
// Indexed feature lookup (mov rdi,[rsi+rdi*8+0x1510]). The signature is the whole faulting instruction, so the repair skips exactly past it with RDI zeroed, the function clamps that to invalid itself.
    { 0x5849BBull, FIX_ZERO_RDI, "GetFeature-C", { 0x48, 0x8B, 0xBC, 0xFE, 0x10, 0x15, 0x00, 0x00 }, 8 },
// Fixed-size memory copies of device state. A fault here means the source device is gone, so the copy is skipped and the destination keeps its last good data.
    { 0x13E1D2Full, FIX_SKIP, "copy1",  { 0x0F, 0xB6, 0x0A, 0x88 }, 4 },
    { 0x13E1D35ull, FIX_SKIP, "copy16", { 0xF3, 0x0F, 0x6F, 0x02 }, 4 },
    { 0x13E1D11ull, FIX_SKIP, "copy2",  { 0x0F, 0xB7, 0x0A, 0x66 }, 4 },
    { 0x13E1D1Full, FIX_SKIP, "copy3",  { 0x0F, 0xB7, 0x0A, 0x44 }, 4 },
    { 0x13E1D58ull, FIX_SKIP, "copy4",  { 0x8B, 0x0A, 0x89, 0x08 }, 4 },
    { 0x13E1D60ull, FIX_SKIP, "copy5",  { 0x8B, 0x0A, 0x44, 0x0F }, 4 },
    { 0x13E1D70ull, FIX_SKIP, "copy6",  { 0x8B, 0x0A, 0x44, 0x0F }, 4 },
    { 0x13E1D80ull, FIX_SKIP, "copy7",  { 0x8B, 0x0A, 0x44, 0x0F }, 4 },
    { 0x13E1D18ull, FIX_SKIP, "copy8",  { 0x48, 0x8B, 0x0A, 0x48 }, 4 },
    { 0x13E1DB0ull, FIX_SKIP, "copy9",  { 0x4C, 0x8B, 0x02, 0x0F }, 4 },
    { 0x13E1DC0ull, FIX_SKIP, "copy10", { 0x4C, 0x8B, 0x02, 0x0F }, 4 },
    { 0x13E1D40ull, FIX_SKIP, "copy11", { 0x4C, 0x8B, 0x02, 0x0F }, 4 },
    { 0x13E1DD0ull, FIX_SKIP, "copy12", { 0x4C, 0x8B, 0x02, 0x8B }, 4 },
    { 0x13E1D98ull, FIX_SKIP, "copy13", { 0x4C, 0x8B, 0x02, 0x8B }, 4 },
    { 0x13E1DE0ull, FIX_SKIP, "copy14", { 0x4C, 0x8B, 0x02, 0x8B }, 4 },
    { 0x13E1E00ull, FIX_SKIP, "copy15", { 0x4C, 0x8B, 0x02, 0x8B }, 4 },
};
#define SITE_COUNT (sizeof(kSites) / sizeof(kSites[0]))

static GuardSite g_sites[MAX_SITES];
static int g_siteCount = 0;

static volatile int g_catchCount[3];
static volatile int g_enabled = 1;
static Addr g_base = 0;
static unsigned char g_safeBuffer[256];
static char g_status[STATUS_LEN] = "[XRInputGuard] not initialized";

static void SetStatus(const char *msg)
{
    size_t n = strlen(msg);
    if (n >= STATUS_LEN)
        n = STATUS_LEN - 1;
    memcpy(g_status, msg, n);
    g_status[n] = '\0';
}

static int MatchBytes(Addr rip, const unsigned char *want, int len)
{
    const unsigned char *p = (const unsigned char *)rip;
    int i;
    __try
    {
        for (i = 0; i < len; i++)
        {
            if (p[i] != want[i])
                return 0;
        }
        return 1;
    }
    __except (1)
    {
        return 0;
    }
}

static void GuardInit(void)
{
    unsigned i;
    int helpers = 0, getfeature = 0;
    char msg[STATUS_LEN];
    HMODULE mod = GetModuleHandleW(L"UnityPlayer.dll");

    g_siteCount = 0;
    if (mod == NULL)
    {
        SetStatus("[XRInputGuard] DISARMED: UnityPlayer.dll not loaded (editor?)");
        return;
    }
    g_base = (Addr)mod;

    for (i = 0; i < SITE_COUNT; i++)
    {
        Addr abs = g_base + kSites[i].rva;
        unsigned s;
        if (!MatchBytes(abs, kSites[i].sig, kSites[i].siglen))
        {
            snprintf(msg, sizeof(msg), "[XRInputGuard] DISARMED: site %s mismatch (Unity build changed?)", kSites[i].name);
            SetStatus(msg);
            g_siteCount = 0;
            return;
        }
        if (kSites[i].fix == FIX_ZERO_RDI)
        {
            s = (unsigned)kSites[i].siglen;
        }
        else
        {
            for (s = 0; s <= MAX_SCAN_AHEAD; s++)
            {
                __try
                {
                    if (((const unsigned char *)abs)[s] == 0xC3)
                        break;
                }
                __except (1)
                {
                    break;
                }
            }
            if (s > MAX_SCAN_AHEAD)
            {
                snprintf(msg, sizeof(msg), "[XRInputGuard] DISARMED: site %s has no ret within %d bytes", kSites[i].name, MAX_SCAN_AHEAD);
                SetStatus(msg);
                g_siteCount = 0;
                return;
            }
        }
        g_sites[g_siteCount].rva = kSites[i].rva;
        g_sites[g_siteCount].skip = s;
        g_sites[g_siteCount].fix = kSites[i].fix;
        g_siteCount++;
        if (kSites[i].fix == FIX_SKIP)
            helpers++;
        else
            getfeature++;
    }

    snprintf(msg, sizeof(msg), "[XRInputGuard] ARMED: helpers=%d getfeature=%d sites=%d", helpers, getfeature, g_siteCount);
    SetStatus(msg);
}

static int HandleFault(Addr rip, Addr *outRip, Addr *outRax, Addr *outRdi, int *outZeroRdi)
{
    int i;
    Addr rva;

    if (!g_enabled || g_siteCount == 0)
        return 0;
    if (rip < g_base)
        return 0;
    rva = rip - g_base;

    for (i = 0; i < g_siteCount; i++)
    {
        unsigned k;
        if (rva != g_sites[i].rva)
            continue;
        for (k = 0; k < SITE_COUNT; k++)
        {
            if (kSites[k].rva != rva)
                continue;
            if (!MatchBytes(rip, kSites[k].sig, kSites[k].siglen))
                return 0;
            if (g_sites[i].fix == FIX_ZERO_PTR)
            {
                *outRax = (Addr)g_safeBuffer;
                *outRip = rip + g_sites[i].skip;
                g_catchCount[0]++;
                return 1;
            }
            if (g_sites[i].fix == FIX_ZERO_RDI)
            {
                *outRdi = 0;
                *outZeroRdi = 1;
                *outRip = rip + g_sites[i].skip;
                g_catchCount[2]++;
                return 1;
            }
            *outRip = rip + g_sites[i].skip;
            g_catchCount[1]++;
            return 1;
        }
        return 0;
    }
    return 0;
}

static LONG CALLBACK VehHandler(EXCEPTION_POINTERS *ep)
{
    Addr rip, outRip = 0, outRax = 0, outRdi = 0;
    int zeroRdi = 0;
    if (ep->ExceptionRecord->ExceptionCode != EXCEPTION_ACCESS_VIOLATION)
        return EXCEPTION_CONTINUE_SEARCH;
    rip = (Addr)ep->ContextRecord->Rip;
    if (HandleFault(rip, &outRip, &outRax, &outRdi, &zeroRdi))
    {
        ep->ContextRecord->Rip = (DWORD64)outRip;
        if (outRax != 0)
            ep->ContextRecord->Rax = (DWORD64)outRax;
        if (zeroRdi)
            ep->ContextRecord->Rdi = (DWORD64)outRdi;
        return EXCEPTION_CONTINUE_EXECUTION;
    }
    return EXCEPTION_CONTINUE_SEARCH;
}

BOOL APIENTRY DllMain(HMODULE mod, DWORD reason, LPVOID reserved)
{
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(mod);
        GuardInit();
        AddVectoredExceptionHandler(1, VehHandler);
    }
    return TRUE;
}

#define EXPORT __declspec(dllexport)
#define CALL __stdcall

#ifdef __cplusplus
extern "C" {
#endif

EXPORT int CALL XRGuard_GetCatchCount(int site)
{
    if (site < 0 || site > 2)
        return -1;
    return InterlockedCompareExchange(&g_catchCount[site], 0, 0);
}

EXPORT int CALL XRGuard_IsEnabled(void)
{
    return InterlockedCompareExchange(&g_enabled, 1, 1);
}

EXPORT void CALL XRGuard_SetEnabled(int enabled)
{
    InterlockedExchange(&g_enabled, enabled ? 1 : 0);
}

EXPORT unsigned long long CALL XRGuard_PlayerBase(void)
{
    return (unsigned long long)g_base;
}

EXPORT const char *CALL XRGuard_StatusString(void)
{
    return g_status;
}

#ifdef __cplusplus
}
#endif
