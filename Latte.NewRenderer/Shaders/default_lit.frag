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

// Specialized constant for maximum light count.
layout (constant_id = 1) const int MAX_LIGHTS = 10;

layout (set = 0, binding = 1) uniform sceneData
{
	vec4 AmbientLightColor; // W for intensity.
	int LightCount;
} SceneData;

struct LightData
{
	vec4 position; // W is ignored.
	vec4 color; // W is intensity.
};

// All light data.
layout (std140, set = 0, binding = 3) readonly buffer lightBuffer
{
	LightData lights[MAX_LIGHTS];
} LightBuffer;

void main()
{
	vec3 diffuseLight = SceneData.AmbientLightColor.xyz * SceneData.AmbientLightColor.w;
	vec3 surfaceNormal = normalize(inNormalWorld);

	for (int i = 0; i < SceneData.LightCount; i++)
	{
		LightData light = LightBuffer.lights[i];
		vec3 directionToLight = light.position.xyz - inPosWorld;
		float attenuation = 1.0 / dot(directionToLight, directionToLight);
		float cosAngIncidence = max(dot(surfaceNormal, normalize(directionToLight)), 0);
		vec3 intensity = light.color.xyz * light.color.w * attenuation;

		diffuseLight += intensity * cosAngIncidence;
	}

	outColor = vec4(diffuseLight * inColor, 1);
}