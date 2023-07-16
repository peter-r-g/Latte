struct VSInput
{
	[[vk::location(0)]] float3 Position : POSITION0;
	[[vk::location(2)]] float2 TextureCoordinates : TEXCOORD0;
};

struct VSOutput
{
	float4 Position : SV_POSITION;
	[[vk::location(0)]] float2 FragmentTextureCoordinates : TEXCOORD0;
};

struct UniformBufferObject
{
	float4x4 View;
	float4x4 Projection;
};

cbuffer ubo : register( b0, space0 )
{
	UniformBufferObject ubo;
};

struct PushConstants
{
	float4x4 Model;
};
[[vk::push_constant]] PushConstants pushConstants;

VSOutput main( VSInput input )
{
	VSOutput output = (VSOutput) 0;
	output.Position = mul( ubo.Projection, mul( ubo.View, mul( pushConstants.Model, float4( input.Position, 1.0f ) ) ) );
	output.FragmentTextureCoordinates = input.TextureCoordinates;
	
	return output;
}
