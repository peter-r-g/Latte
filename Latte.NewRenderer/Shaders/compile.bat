@ECHO OFF
%VULKAN_SDK%\Bin\glslc.exe colored_triangle.vert -o colored_triangle.vert.spv
%VULKAN_SDK%\Bin\glslc.exe colored_triangle.frag -o colored_triangle.frag.spv
%VULKAN_SDK%\Bin\glslc.exe triangle.vert -o triangle.vert.spv
%VULKAN_SDK%\Bin\glslc.exe triangle.frag -o triangle.frag.spv
%VULKAN_SDK%\Bin\glslc.exe mesh_triangle.vert -o mesh_triangle.vert.spv
%VULKAN_SDK%\Bin\glslc.exe default_lit.frag -o default_lit.frag.spv
%VULKAN_SDK%\Bin\glslc.exe textured_lit.frag -o textured_lit.frag.spv