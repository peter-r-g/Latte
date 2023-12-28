#version 450 core

// Input.
layout (location = 0) in vec2 vPosition;
layout (location = 1) in vec2 vTexCoord;
layout (location = 2) in vec4 vColor;

// Output.
out gl_PerVertex { vec4 gl_Position; };
layout (location = 0) out struct { vec4 Color; vec2 UV; } Out;

layout (push_constant) uniform uPushConstant { vec2 uScale; vec2 uTranslate; } pc;

void main()
{
	Out.Color = vColor;
	Out.UV = vTexCoord;
	gl_Position = vec4(vPosition * pc.uScale + pc.uTranslate, 0, 1);
}