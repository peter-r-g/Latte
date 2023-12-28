#version 450 core
#extension GL_KHR_vulkan_glsl : enable

// Input.
layout (location = 0) in struct { vec4 Color; vec2 UV; } In;

// Output.
layout (location = 0) out vec4 outColor;

layout (set = 0, binding = 0) uniform sampler2D sTexture;

void main()
{
	outColor = In.Color * texture(sTexture, In.UV.st);
}