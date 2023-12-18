@ECHO OFF
mkdir ..\bin\Release\net8.0\Assets\Shaders

%VULKAN_SDK%\Bin\glslc.exe colored_triangle.vert -o ..\bin\Release\net8.0\Assets\Shaders\colored_triangle.vert.spv
%VULKAN_SDK%\Bin\glslc.exe colored_triangle.frag -o ..\bin\Release\net8.0\Assets\Shaders\colored_triangle.frag.spv
%VULKAN_SDK%\Bin\glslc.exe triangle.vert -o ..\bin\Release\net8.0\Assets\Shaders\triangle.vert.spv
%VULKAN_SDK%\Bin\glslc.exe triangle.frag -o ..\bin\Release\net8.0\Assets\Shaders\triangle.frag.spv
%VULKAN_SDK%\Bin\glslc.exe mesh_triangle.vert -o ..\bin\Release\net8.0\Assets\Shaders\mesh_triangle.vert.spv
%VULKAN_SDK%\Bin\glslc.exe default_lit.frag -o ..\bin\Release\net8.0\Assets\Shaders\default_lit.frag.spv
%VULKAN_SDK%\Bin\glslc.exe textured_lit.frag -o ..\bin\Release\net8.0\Assets\Shaders\textured_lit.frag.spv