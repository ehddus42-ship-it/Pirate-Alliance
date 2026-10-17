// fBm gradient noise standing in for Blender's Noise Texture (4D, normalized "Fac" ~ 0..1 centred on 0.5).
// W (the 4th dimension Blender animates for evolution) slides the 3D slice through the field.
#ifndef PROI_BLAZE_NOISE_INCLUDED
#define PROI_BLAZE_NOISE_INCLUDED

float3 BlazeHash3(float3 p)
{
    p = float3(dot(p, float3(127.1, 311.7, 74.7)), dot(p, float3(269.5, 183.3, 246.1)), dot(p, float3(113.5, 271.9, 124.6)));
    return -1.0 + 2.0 * frac(sin(p) * 43758.5453123);
}

float BlazeGradient(float3 p)        // ~ -1 .. 1
{
    float3 i = floor(p), f = frac(p);
    float3 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
    float n000 = dot(BlazeHash3(i + float3(0, 0, 0)), f - float3(0, 0, 0));
    float n100 = dot(BlazeHash3(i + float3(1, 0, 0)), f - float3(1, 0, 0));
    float n010 = dot(BlazeHash3(i + float3(0, 1, 0)), f - float3(0, 1, 0));
    float n110 = dot(BlazeHash3(i + float3(1, 1, 0)), f - float3(1, 1, 0));
    float n001 = dot(BlazeHash3(i + float3(0, 0, 1)), f - float3(0, 0, 1));
    float n101 = dot(BlazeHash3(i + float3(1, 0, 1)), f - float3(1, 0, 1));
    float n011 = dot(BlazeHash3(i + float3(0, 1, 1)), f - float3(0, 1, 1));
    float n111 = dot(BlazeHash3(i + float3(1, 1, 1)), f - float3(1, 1, 1));
    return lerp(lerp(lerp(n000, n100, u.x), lerp(n010, n110, u.x), u.y),
                lerp(lerp(n001, n101, u.x), lerp(n011, n111, u.x), u.y), u.z);
}

float BlazeFbm(float3 p, float octaves, float roughness)
{
    float sum = 0.0, norm = 0.0, amp = 1.0;
    [unroll(6)] for (int k = 0; k < 6; k++)
    {
        if (k >= octaves) break;
        sum += amp * BlazeGradient(p);
        norm += amp;
        amp *= roughness;
        p = p * 2.03 + 17.17;
    }
    return sum / norm;
}

float BlazeNoise4(float3 p, float w, float octaves, float roughness, float distortion)
{
    p += float3(0.37, -0.53, 0.71) * w * 2.0;
    if (distortion > 0.0)
        p += distortion * float3(BlazeGradient(p + 5.2), BlazeGradient(p + 11.7), BlazeGradient(p + 23.9));
    return saturate(0.5 + 0.9 * BlazeFbm(p, octaves, roughness));
}

#endif
