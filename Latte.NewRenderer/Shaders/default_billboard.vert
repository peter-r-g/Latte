#version 450
#extension GL_KHR_vulkan_glsl : enable

// Input.
layout (location = 0) in vec3 vPosition;
layout (location = 1) in vec3 vNormal;
layout (location = 2) in vec3 vColor;
layout (location = 3) in vec2 vTexCoord;

// Output.
layout (location = 0) out vec2 outOffset;

// Specialized constant for maximum object count.
layout (constant_id = 0) const int MAX_OBJECTS = 10000;

layout (set = 0, binding = 0) uniform CameraBuffer
{
	mat4 view;
	mat4 projection;
	mat4 viewproj;
} CameraData;

struct ObjectData
{
	mat4 model;
};

// All object matrices
layout (std140, set = 0, binding = 2) readonly buffer objectBuffer
{
	ObjectData objects[MAX_OBJECTS];
} ObjectBuffer;

const float BILLBOARD_RADIUS = 0.1;

void main()
{
	outOffset = vPosition.xz;
	vec3 cameraRightWorld = {CameraData.view[0][0], CameraData.view[1][0], CameraData.view[2][0]};
	vec3 cameraUpWorld = {CameraData.view[0][1], CameraData.view[1][1], CameraData.view[2][1]};

	mat4 modelMatrix = ObjectBuffer.objects[gl_InstanceIndex].model;
	vec3 modelPosition = {modelMatrix[3][0], modelMatrix[3][1], modelMatrix[3][2]};
	vec3 positionWorld = modelPosition
	+ BILLBOARD_RADIUS * outOffset.x * cameraRightWorld
	+ BILLBOARD_RADIUS * outOffset.y * cameraUpWorld;

	gl_Position = CameraData.viewproj * vec4(positionWorld, 1.0);
}