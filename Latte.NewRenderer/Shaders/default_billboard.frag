#version 450

layout (location = 0) in vec2 inOffset;

layout (location = 0) out vec4 outColor;

layout(set = 0, binding = 1) uniform sceneData
{
	vec4 AmbientLightColor; // W for intensity.
	vec4 SunPosition; // W is ignored.
	vec4 SunLightColor; // W for intensity.
} SceneData;

void main()
{
	float dis = sqrt(dot(inOffset, inOffset));
	if (dis >= 1.0)
		discard;

	outColor = vec4(SceneData.SunLightColor.xyz, 1);
}