@ECHO OFF
%VULKAN_SDK%\Bin\glslc.exe colored_triangle.vert -o ..\bin\Release\net8.0\Assets\colored_triangle.vert.spv
%VULKAN_SDK%\Bin\glslc.exe colored_triangle.frag -o ..\bin\Release\net8.0\Assets\colored_triangle.frag.spv
%VULKAN_SDK%\Bin\glslc.exe triangle.vert -o ..\bin\Release\net8.0\Assets\triangle.vert.spv
%VULKAN_SDK%\Bin\glslc.exe triangle.frag -o ..\bin\Release\net8.0\Assets\triangle.frag.spv
%VULKAN_SDK%\Bin\glslc.exe mesh_triangle.vert -o ..\bin\Release\net8.0\Assets\mesh_triangle.vert.spv
%VULKAN_SDK%\Bin\glslc.exe default_lit.frag -o ..\bin\Release\net8.0\Assets\default_lit.frag.spv
%VULKAN_SDK%\Bin\glslc.exe textured_lit.frag -o ..\bin\Release\net8.0\Assets\textured_lit.frag.spv