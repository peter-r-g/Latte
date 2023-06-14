#version 450

layout(binding = 0) uniform UniformBufferObject {
	mat4 View;
	mat4 Projection;
} ubo;

layout(push_constant) uniform constants
{
	mat4 Model;
} PushConstants;

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inColor;
layout(location = 2) in vec2 inTextureCoordinates;

layout(location = 0) out vec3 fragColor;
layout(location = 1) out vec2 fragTextureCoordinates;

void main() {
	gl_Position = ubo.Projection * ubo.View * PushConstants.Model * vec4(inPosition, 1.0f);
	fragColor = inColor;
	fragTextureCoordinates = inTextureCoordinates;
}