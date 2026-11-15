// Compile via: fxc.exe /T ps_3_0 /E main /O3 /Fo PlasmaOrbEffect.ps PlasmaOrbEffect.fx
sampler2D implicitInput : register(s0);
float Time : register(c0);
float AudioEnergy : register(c1);
float4 PrimaryColor : register(c2);
float4 SecondaryColor : register(c3);

float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453123); }

float noise(float2 p)
{
    float2 i = floor(p), f = frac(p), u = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(hash(i), hash(i + float2(1, 0)), u.x),
                lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), u.x), u.y);
}

float fbm(float2 p)
{
    float v = 0.0, a = 0.5;
    float2x2 rot = float2x2(0.8, 0.6, -0.6, 0.8);
    for (int i = 0; i < 4; i++) { v += a * noise(p); p = mul(rot, p) * 2.05; a *= 0.5; }
    return v;
}

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float2 p = (uv - 0.5) * 2.0;
    float r = length(p);
    if (r > 1.0) return float4(0, 0, 0, 0);

    float z = sqrt(saturate(1.0 - r * r));
    float angle = atan2(p.y, p.x);

    float t = Time * (1.4 + AudioEnergy * 2.8);
    float f1 = fbm(float2(angle * 3.0 + t * 0.7, r * 4.5 - t));
    float f2 = fbm(float2(angle * 5.0 - t * 1.1, r * 6.0 + t * 0.8));

    float arc = 1.0 - smoothstep(0.0, 0.09 + AudioEnergy * 0.08, abs(f1 - f2));
    float core = exp(-r * (3.2 - AudioEnergy * 1.4));
    float fresnel = pow(1.0 - z, 3.0);

    float3 col = lerp(PrimaryColor.rgb, SecondaryColor.rgb, f1);
    float3 finalRgb = col * (core * 1.3 + arc * (1.8 + AudioEnergy * 2.0)) + SecondaryColor.rgb * fresnel * 1.1;
    float alpha = saturate(smoothstep(1.0, 0.88, r) * (0.28 + core * 0.72 + arc * 0.85 + fresnel * 0.65));

    return float4(finalRgb * alpha, alpha);
}