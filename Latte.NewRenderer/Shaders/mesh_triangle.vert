#version 460
#extension GL_KHR_vulkan_glsl : enable

layout (location = 0) in vec3 vPosition;
layout (location = 1) in vec3 vNormal;
layout (location = 2) in vec3 vColor;
layout (location = 3) in vec2 vTexCoord;

layout (location = 0) out vec3 outColor;
layout (location = 1) out vec2 texCoord;

layout(set = 0, binding = 0) uniform CameraBuffer{
	mat4 view;
	mat4 projection;
	mat4 viewproj;
} CameraData;

struct ObjectData{
	mat4 model;
};

//all object matrices
layout(std140, set = 0, binding = 2) readonly buffer objectBuffer {

	ObjectData objects[];
} ObjectBuffer;

void main()
{
	mat4 modelMatrix = ObjectBuffer.objects[gl_InstanceIndex].model;
	mat4 transformMatrix = (CameraData.viewproj * modelMatrix);

	gl_Position = transformMatrix * vec4(vPosition, 1.0f);
	outColor = vColor;
	texCoord = vTexCoord;
}