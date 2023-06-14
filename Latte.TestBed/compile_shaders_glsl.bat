@ECHO OFF

C:\VulkanSDK\1.3.224.1\Bin\glslc.exe -fshader-stage=vert Assets\Shaders\vert.glsl -o Assets\Shaders\vert.spv
C:\VulkanSDK\1.3.224.1\Bin\glslc.exe -fshader-stage=frag Assets\Shaders\frag.glsl -o Assets\Shaders\frag.spv

PAUSE