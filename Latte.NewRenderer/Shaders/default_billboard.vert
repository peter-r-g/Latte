#version 450
#extension GL_KHR_vulkan_glsl : enable

layout (location = 0) in vec3 vPosition;
layout (location = 1) in vec3 vNormal;
layout (location = 2) in vec3 vColor;
layout (location = 3) in vec2 vTexCoord;

layout (location = 0) out vec2 outOffset;

layout(set = 0, binding = 0) uniform CameraBuffer
{
	mat4 view;
	mat4 projection;
	mat4 viewproj;
} CameraData;

layout(set = 0, binding = 1) uniform sceneData
{
	vec4 AmbientLightColor; // W for intensity.
	vec4 SunPosition; // W is ignored.
	vec4 SunLightColor; // W for intensity.
} SceneData;

const float LIGHT_RADIUS = 0.1;

void main()
{
	outOffset = vPosition.xz;
	vec3 cameraRightWorld = {CameraData.view[0][0], CameraData.view[1][0], CameraData.view[2][0]};
	vec3 cameraUpWorld = {CameraData.view[0][1], CameraData.view[1][1], CameraData.view[2][1]};

	vec3 positionWorld = SceneData.SunPosition.xyz
	+ LIGHT_RADIUS * outOffset.x * cameraRightWorld
	+ LIGHT_RADIUS * outOffset.y * cameraUpWorld;

	gl_Position = CameraData.viewproj * vec4(positionWorld, 1.0);
}