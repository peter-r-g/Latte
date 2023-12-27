@ECHO OFF
mkdir ..\bin\Debug\net8.0\Assets\Shaders

%VULKAN_SDK%\Bin\glslc.exe colored_triangle.vert -o ..\bin\Debug\net8.0\Assets\Shaders\colored_triangle.vert.spv
%VULKAN_SDK%\Bin\glslc.exe colored_triangle.frag -o ..\bin\Debug\net8.0\Assets\Shaders\colored_triangle.frag.spv
%VULKAN_SDK%\Bin\glslc.exe triangle.vert -o ..\bin\Debug\net8.0\Assets\Shaders\triangle.vert.spv
%VULKAN_SDK%\Bin\glslc.exe triangle.frag -o ..\bin\Debug\net8.0\Assets\Shaders\triangle.frag.spv
%VULKAN_SDK%\Bin\glslc.exe mesh_triangle.vert -o ..\bin\Debug\net8.0\Assets\Shaders\mesh_triangle.vert.spv
%VULKAN_SDK%\Bin\glslc.exe default_lit.frag -o ..\bin\Debug\net8.0\Assets\Shaders\default_lit.frag.spv
%VULKAN_SDK%\Bin\glslc.exe textured_lit.frag -o ..\bin\Debug\net8.0\Assets\Shaders\textured_lit.frag.spv
%VULKAN_SDK%\Bin\glslc.exe imgui.vert -o ..\bin\Debug\net8.0\Assets\Shaders\imgui.vert.spv
%VULKAN_SDK%\Bin\glslc.exe imgui.frag -o ..\bin\Debug\net8.0\Assets\Shaders\imgui.frag.spv
%VULKAN_SDK%\Bin\glslc.exe default_billboard.vert -o ..\bin\Debug\net8.0\Assets\Shaders\default_billboard.vert.spv
%VULKAN_SDK%\Bin\glslc.exe default_billboard.frag -o ..\bin\Debug\net8.0\Assets\Shaders\default_billboard.frag.spv