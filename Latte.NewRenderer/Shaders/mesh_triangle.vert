#version 460
#extension GL_KHR_vulkan_glsl : enable

// Input.
layout (location = 0) in vec3 vPosition;
layout (location = 1) in vec3 vNormal;
layout (location = 2) in vec3 vColor;
layout (location = 3) in vec2 vTexCoord;

// Output.
layout (location = 0) out vec3 outColor;
layout (location = 1) out vec2 outTexCoord;
layout (location = 2) out vec3 outPosWorld;
layout (location = 3) out vec3 outNormalWorld;

layout (set = 0, binding = 0) uniform CameraBuffer
{
	mat4 view;
	mat4 projection;
	mat4 viewproj;
} CameraData;

layout (set = 0, binding = 1) uniform sceneData
{
	vec4 AmbientLightColor; // W for intensity.
	vec4 SunPosition; // W is ignored.
	vec4 SunLightColor; // W for intensity.
} SceneData;

struct ObjectData
{
	mat4 model;
};

// All object matrices.
layout (std140, set = 0, binding = 2) readonly buffer objectBuffer
{
	ObjectData objects[];
} ObjectBuffer;

void main()
{
	mat4 modelMatrix = ObjectBuffer.objects[gl_InstanceIndex].model;
	vec4 positionWorld = modelMatrix * vec4(vPosition, 1.0);
	gl_Position = CameraData.viewproj * positionWorld;

	outColor = vColor;
	outTexCoord = vTexCoord;
	outPosWorld = positionWorld.xyz;
	outNormalWorld = normalize(mat3(modelMatrix) * vNormal);
}