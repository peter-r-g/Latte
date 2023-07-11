@ECHO OFF

C:\VulkanSDK\1.3.224.1\Bin\dxc.exe Assets\Shaders\altfrag.hlsl -T ps_6_7 -fspv-entrypoint-name=main -fspv-target-env=vulkan1.3 -E main -spirv -Fo Assets\Shaders\altfrag.spv
C:\VulkanSDK\1.3.224.1\Bin\dxc.exe Assets\Shaders\altvert.hlsl -T vs_6_7 -fspv-entrypoint-name=main -fspv-target-env=vulkan1.3 -E main -spirv -Fo Assets\Shaders\altvert.spv

PAUSE