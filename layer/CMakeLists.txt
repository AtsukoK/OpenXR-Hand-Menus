cmake_minimum_required(VERSION 3.20)
project(VRRadialOverlay CXX)
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
add_library(VRRadialOverlay SHARED
    src/layer.cpp src/engine.cpp src/keyboard.cpp src/wrist.cpp src/config.cpp src/keys.cpp src/panel.cpp src/common.cpp)
target_include_directories(VRRadialOverlay PRIVATE
    ${CMAKE_SOURCE_DIR}/../third_party/openxr-sdk/include
    ${CMAKE_SOURCE_DIR}/../third_party)
target_compile_definitions(VRRadialOverlay PRIVATE UNICODE _UNICODE)
target_compile_options(VRRadialOverlay PRIVATE /W3 /EHsc /utf-8)
target_link_libraries(VRRadialOverlay PRIVATE d3d11 d3d12 gdiplus shell32 ole32 user32)

add_executable(vro_test test/test.cpp
    src/engine.cpp src/keyboard.cpp src/wrist.cpp src/config.cpp src/keys.cpp src/panel.cpp src/common.cpp)
target_include_directories(vro_test PRIVATE
    ${CMAKE_SOURCE_DIR}/../third_party/openxr-sdk/include
    ${CMAKE_SOURCE_DIR}/../third_party)
target_compile_definitions(vro_test PRIVATE UNICODE _UNICODE)
target_compile_options(vro_test PRIVATE /EHsc /utf-8)
target_link_libraries(vro_test PRIVATE d3d11 d3d12 gdiplus shell32 ole32 user32)
