# XRInputGuard build (Windows-only, MSVC).
#
#   cmake -B build -DCMAKE_BUILD_TYPE=Release
#   cmake --build build --config Release
#
# Output lands directly in Assets/Plugins/x86_64 so Unity picks it up on the next refresh.
cmake_minimum_required(VERSION 3.16)
project(XRInputGuard C)

set(CMAKE_C_STANDARD 99)
set(CMAKE_C_STANDARD_REQUIRED ON)

add_library(XRInputGuard SHARED XRInputGuard.c)

# Self-contained: no VC++ redistributable dependency for players.
set_property(TARGET XRInputGuard PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded")
target_compile_options(XRInputGuard PRIVATE /W3 /O2)

set_target_properties(XRInputGuard PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}/../../Assets/Plugins/x86_64"
  RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CMAKE_CURRENT_SOURCE_DIR}/../../Assets/Plugins/x86_64"
  RUNTIME_OUTPUT_DIRECTORY_DEBUG "${CMAKE_CURRENT_SOURCE_DIR}/../../Assets/Plugins/x86_64"
  ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}")
