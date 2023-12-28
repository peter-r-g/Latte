//glsl version 4.5
#version 450
#extension GL_KHR_vulkan_glsl : enable

// Input.
layout (location = 0) in vec3 inColor;
layout (location = 1) in vec2 inTexCoord;
layout (location = 2) in vec3 inPosWorld;
layout (location = 3) in vec3 inNormalWorld;

// Output.
layout (location = 0) out vec4 outColor;

layout (set = 0, binding = 1) uniform sceneData
{
	vec4 AmbientLightColor; // W for intensity.
	vec4 SunPosition; // W is ignored.
	vec4 SunLightColor; // W for intensity.
} SceneData;

void main()
{
	vec3 directionToLight = SceneData.SunPosition.xyz - inPosWorld;
	float attenuation = 1.0 / dot(directionToLight, directionToLight);

	vec3 sunLightColor = SceneData.SunLightColor.xyz * SceneData.SunLightColor.w * attenuation;
	vec3 ambientLightColor = SceneData.AmbientLightColor.xyz * SceneData.AmbientLightColor.w;
	vec3 diffuseLight = sunLightColor * max(dot(normalize(inNormalWorld), normalize(directionToLight)), 0);

	outColor = vec4((diffuseLight + ambientLightColor) * inColor, 1);
}