Shader "EmbersEdge/BlueprintFill2D"
{
    // Ore-built construction: a placed building stands as a schematic of its own art and the real
    // pixels PRINT IN as ore arrives (_Fill 0..1, driven per renderer by GhostIntake). _Style picks
    // one of six looks (BM.blueprintStyle — switchable live while playing):
    //   0 Schematic   dark silhouette, crisp outline, diagonal hatch, slow light sweep; bottom-up raster print
    //   1 Hologram    rolling scanlines + flicker, glowing outline; prints outward from the centre
    //   2 Draft       wireframe: outline + faint 4-texel grid, no body; diagonal wipe with an ink flash
    //   3 Materialise near-invisible body with twinkling motes; dark-first dissolve, each pixel flashes in
    //   4 Rings       breathing body with rising energy lines; prints in rings from the base, hot frontier
    //   5 Blocks      faint checker; prints in chunky 4x4 blocks that flash as they land
    //   6 Fusion        hologram body (scanlines, flicker, breathing) printed in 4x4 blocks that land in
    //                   rings from the footprint's base; hot chunky frontier with a trailing glow
    //   7 FusionLattice hologram body drawn as a 4x4 block lattice; prints pixel-by-pixel in rings, and
    //                   the blocks just ahead of the frontier charge up (pulse bright) before they print
    //   8 FusionPulse   hologram body with a radar pulse rolling outward from the base through it;
    //                   prints in shuffled bottom-up 4x4 blocks that flash as they land
    //   9 Decompress    FusionLattice from the CENTRE out, printed like the old SpriteDecompressor: dark
    //                   pixels arrive first and bright ones last, and pixels just ahead of the frontier
    //                   sparkle in and out at random over time before they settle; the block lattice
    //                   charges ahead of the print
    // _Brightness scales the unprinted body (the "schematic" part) — the outline and print head are not
    // dimmed by it. Unlit on purpose; the building's own material returns on completion. The pass has no
    // LightMode tag → the URP 2D renderer draws it as SRPDefaultUnlit. NOTE: never name a variable
    // `real`/`half`/`line`/`point` in here — URP/HLSL reserve them.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Fill        ("Fill", Range(0, 1)) = 0
        _Active      ("Activity (ore landing now)", Range(0, 1)) = 0
        _Progress    ("Progress (share of pixels printed)", Range(0, 1)) = 0
        _Tint        ("Printed Pixel Era Tint", Range(0, 1)) = 0.3
        _Sheen       ("Printed Sheen Strength", Range(0, 1)) = 0.6
        _SheenSpeed  ("Sheen Speed", Float) = 0.35
        _SheenTiles  ("Sheen Bands Across", Float) = 1.5
        _SheenWobble ("Sheen Refraction (texels)", Range(0, 2)) = 0.6
        _Style       ("Style (0-9)", Float) = 0
        _Brightness  ("Body Brightness", Range(0, 2)) = 0.6
        _Rect        ("Sprite UV Rect (x,y,w,h)", Vector) = (0, 0, 1, 1)
        _Blueprint   ("Body Tint", Color) = (0.22, 0.18, 0.4, 0.32)
        _Outline     ("Outline / Glow", Color) = (0.72, 0.58, 1.0, 0.95)
        _Hatch       ("Hatch Alpha", Range(0, 1)) = 0.14
        _HatchPeriod ("Hatch Period (texels)", Float) = 6
        _Jitter      ("Print Edge Jitter", Range(0, 0.5)) = 0.07
        _HeadWidth   ("Print Head Width", Range(0, 0.2)) = 0.03
        _Sweep       ("Sweep Strength", Range(0, 1)) = 0.3
        _SweepSpeed  ("Sweep Speed", Float) = 0.3
        [PerRendererData] _RendererColor ("Renderer Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "Queue"            = "Transparent"
            "RenderType"       = "Transparent"
            "RenderPipeline"   = "UniversalPipeline"
            "IgnoreProjector"  = "True"
            "PreviewType"      = "Plane"
            "CanUseSpriteAtlas"= "True"
        }

        Cull Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _MainTex_TexelSize;
                float  _Fill;
                float  _Active;
                float  _Progress;
                float  _Tint;
                float  _Sheen;
                float  _SheenSpeed;
                float  _SheenTiles;
                float  _SheenWobble;
                float  _Style;
                float  _Brightness;
                float4 _Rect;
                float4 _Blueprint;
                float4 _Outline;
                float  _Hatch;
                float  _HatchPeriod;
                float  _Jitter;
                float  _HeadWidth;
                float  _Sweep;
                float  _SweepSpeed;
                float4 _RendererColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.color = IN.color * _RendererColor;
                return OUT;
            }

            float Hash2(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }
            float Hash1(float x)  { return frac(sin(x * 91.7) * 43758.5453); }

            // smooth value noise (Perlin-style, hash lattice + smoothstep blend) and two octaves of it
            float Noise2(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash2(i), b = Hash2(i + float2(1, 0)), c = Hash2(i + float2(0, 1)), d = Hash2(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }
            float Fbm2(float2 p) { return Noise2(p) * 0.65 + Noise2(p * 2.3 + 7.1) * 0.35; }
            float GlossBand(float x) { return pow(saturate(1.0 - abs(frac(x) * 2.0 - 1.0)), 5.0); }

            // alpha of a neighbouring texel, 0 outside the sprite's own rect (atlas neighbours never bleed in)
            float NeighbourAlpha(float2 uv)
            {
                float2 lo = _Rect.xy, hi = _Rect.xy + _Rect.zw;
                if (uv.x < lo.x || uv.y < lo.y || uv.x > hi.x || uv.y > hi.y) return 0.0;
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).a;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                if (tex.a <= 0.002) return half4(0, 0, 0, 0);

                int   style = (int)round(_Style);
                float t     = _Time.y;
                float2 texel = floor(IN.uv * _MainTex_TexelSize.zw);
                float2 luv   = saturate((IN.uv - _Rect.xy) / max(_Rect.zw, 1e-5));   // 0..1 inside the sprite
                float  h     = Hash2(texel);
                float  lum   = dot(tex.rgb, float3(0.299, 0.587, 0.114));

                // ---- outline: one texel where the sprite meets transparency
                float2 ts = _MainTex_TexelSize.xy;
                float nb = min(min(NeighbourAlpha(IN.uv + float2(ts.x, 0)), NeighbourAlpha(IN.uv - float2(ts.x, 0))),
                               min(NeighbourAlpha(IN.uv + float2(0, ts.y)), NeighbourAlpha(IN.uv - float2(0, ts.y))));
                float edge = step(nb, 0.5);

                // ---- per style: print ORDER (0..1, printed when <= _Fill), body colour/alpha, outline strength
                float  order    = 0.0;
                float3 bodyRGB  = _Blueprint.rgb;
                float  bodyA    = _Blueprint.a;
                float  outlineA = _Outline.a;
                float3 edgeRGB  = _Outline.rgb;   // colour of the 1-texel contour
                float3 glowRGB  = _Outline.rgb;   // colour of print-head / flash / sparkle
                float  headK    = 0.85;           // how hard the print head band glows
                float  flash    = 0.0;            // extra glow on pixels that just printed (0..1)
                float  contour  = 0.0;            // era-coloured boundary contour strength (0..1)
                float  preview  = 0.0;            // translucent premonition of pixels about to print (0..1)

                if (style == 1)
                {
                    // HOLOGRAM: outward from the centre; rolling scanlines + a nervous flicker
                    order = saturate(length((luv - 0.5) * 2.0) * (1.0 - _Jitter) + h * _Jitter);
                    float scan  = step(0.5, frac(luv.y * 36.0 - t * 1.6));
                    float flick = 0.82 + 0.18 * Hash1(floor(t * 14.0));
                    bodyA   = _Blueprint.a * (0.55 + 0.45 * scan) * flick;
                    bodyRGB = lerp(_Blueprint.rgb, _Outline.rgb, 0.35 * scan);
                    outlineA = _Outline.a * flick;
                }
                else if (style == 2)
                {
                    // DRAFT: wireframe — no body, just the outline and a faint 4-texel grid; diagonal wipe
                    order = saturate((luv.x + luv.y) * 0.5 * (1.0 - _Jitter) + h * _Jitter);
                    float gx = step(fmod(texel.x, 4.0), 0.5), gy = step(fmod(texel.y, 4.0), 0.5);
                    float grid = max(gx, gy);
                    bodyA   = _Blueprint.a * (0.18 + 0.55 * grid);
                    bodyRGB = lerp(_Blueprint.rgb, _Outline.rgb, 0.5 * grid);
                    flash   = step(_Fill - 0.06, order) * step(order, _Fill);      // fresh ink
                }
                else if (style == 3)
                {
                    // MATERIALISE: dark-first dissolve; the unprinted body is almost gone, motes twinkle in it
                    order = saturate(lerp(h, lum, 0.5));
                    float mote = step(0.965, Hash2(texel + floor(t * 6.0)));
                    bodyA   = _Blueprint.a * 0.28 + mote * 0.8;
                    bodyRGB = lerp(_Blueprint.rgb, _Outline.rgb, mote);
                    outlineA = _Outline.a * 0.6;
                    flash   = step(_Fill - 0.05, order) * step(order, _Fill);      // each pixel pops in
                }
                else if (style == 4)
                {
                    // RINGS: from the footprint's base outward; the body breathes, energy lines rise through it
                    float2 d = (luv - float2(0.5, 0.0)) * float2(1.0, 1.15);
                    order = saturate(length(d) / 1.15 * (1.0 - _Jitter) + h * _Jitter);
                    float breathe = 0.65 + 0.35 * sin(t * 2.2 + luv.y * 3.0);
                    float rise = step(0.78, frac(luv.y * 5.0 - t * 0.9 + h * 0.15));
                    bodyA   = _Blueprint.a * breathe + rise * 0.22;
                    bodyRGB = lerp(_Blueprint.rgb, _Outline.rgb, rise);
                    flash   = step(_Fill - 0.08, order) * step(order, _Fill) * (1.0 - (_Fill - order) / 0.08);   // trailing glow behind the ring
                }
                else if (style == 5)
                {
                    // BLOCKS: chunky 4x4 blocks land in a shuffled bottom-up order; faint checker meanwhile
                    float2 blk = floor(texel / 4.0);
                    float rowN = saturate((luv.y * _Rect.w * _MainTex_TexelSize.w) / 4.0 / max(1.0, _Rect.w * _MainTex_TexelSize.w / 4.0));
                    order = saturate(rowN * 0.55 + Hash2(blk) * 0.45);
                    float chk = fmod(blk.x + blk.y, 2.0);
                    bodyA   = _Blueprint.a * (0.7 + 0.3 * chk);
                    bodyRGB = _Blueprint.rgb;
                    flash   = step(_Fill - 0.05, order) * step(order, _Fill);      // the block lands
                }
                else if (style == 6)
                {
                    // FUSION: hologram + rings + blocks — a scanlined, flickering, breathing body that prints
                    // in 4x4 blocks landing in rings from the footprint's base; the frontier is a chunky hot
                    // ring with a glow that trails behind it
                    float2 blk = floor(texel / 4.0);
                    float2 blkUv = ((blk + 0.5) * 4.0) * _MainTex_TexelSize.xy;            // block centre in texture uv
                    float2 bl = saturate((blkUv - _Rect.xy) / max(_Rect.zw, 1e-5));       // … in sprite space
                    float2 d = (bl - float2(0.5, 0.0)) * float2(1.0, 1.15);
                    order = saturate(length(d) / 1.15 * 0.85 + Hash2(blk) * 0.15);
                    float scan  = step(0.5, frac(luv.y * 36.0 - t * 1.6));
                    float flick = 0.82 + 0.18 * Hash1(floor(t * 14.0));
                    float breathe = 0.7 + 0.3 * sin(t * 2.2 + luv.y * 3.0);
                    bodyA   = _Blueprint.a * (0.55 + 0.45 * scan) * flick * breathe;
                    bodyRGB = lerp(_Blueprint.rgb, _Outline.rgb, 0.35 * scan);
                    outlineA = _Outline.a * flick;
                    flash   = step(_Fill - 0.08, order) * step(order, _Fill) * (1.0 - (_Fill - order) / 0.08);   // landed blocks glow, fading behind the ring
                }
                else if (style == 7)
                {
                    // FUSION LATTICE: rings print at texel resolution through a block lattice; the blocks
                    // just beyond the frontier charge (throb bright) before the ring reaches them
                    float2 blk = floor(texel / 4.0);
                    float2 blkUv = ((blk + 0.5) * 4.0) * _MainTex_TexelSize.xy;
                    float2 bl = saturate((blkUv - _Rect.xy) / max(_Rect.zw, 1e-5));
                    float2 d = (luv - float2(0.5, 0.0)) * float2(1.0, 1.15);
                    order = saturate(length(d) / 1.15 * (1.0 - _Jitter * 0.5) + h * _Jitter * 0.5);
                    float bo = length((bl - float2(0.5, 0.0)) * float2(1.0, 1.15)) / 1.15;   // the block's ring distance
                    float charging = step(0.001, _Fill) * step(_Fill, bo) * step(bo, _Fill + 0.12);
                    float chk   = fmod(blk.x + blk.y, 2.0);
                    float scan  = step(0.5, frac(luv.y * 36.0 - t * 1.6));
                    float flick = 0.82 + 0.18 * Hash1(floor(t * 14.0));
                    float throb = 0.6 + 0.4 * sin(t * 9.0 + bo * 30.0);
                    bodyA   = _Blueprint.a * (0.5 + 0.3 * chk) * (0.6 + 0.4 * scan) * flick + charging * 0.45 * throb;
                    bodyRGB = lerp(_Blueprint.rgb, _Outline.rgb, max(0.3 * scan, charging));
                    outlineA = _Outline.a * flick;
                    flash   = step(_Fill - 0.05, order) * step(order, _Fill);
                }
                else if (style == 8)
                {
                    // FUSION PULSE: a radar ring rolls outward from the base through the hologram body
                    // (time-driven, independent of progress); blocks land in a shuffled bottom-up order
                    float2 blk = floor(texel / 4.0);
                    float2 blkUv = ((blk + 0.5) * 4.0) * _MainTex_TexelSize.xy;
                    float2 bl = saturate((blkUv - _Rect.xy) / max(_Rect.zw, 1e-5));
                    order = saturate(bl.y * 0.55 + Hash2(blk) * 0.45);
                    float ringD = length((luv - float2(0.5, 0.0)) * float2(1.0, 1.15)) / 1.15;
                    float pulse = 1.0 - smoothstep(0.0, 0.12, abs(frac(t * 0.45) - ringD));
                    float scan  = step(0.5, frac(luv.y * 36.0 - t * 1.6));
                    float flick = 0.85 + 0.15 * Hash1(floor(t * 14.0));
                    bodyA   = _Blueprint.a * (0.5 + 0.3 * scan) * flick + pulse * 0.5;
                    bodyRGB = lerp(_Blueprint.rgb, _Outline.rgb, max(0.3 * scan, pulse * 0.8));
                    outlineA = _Outline.a * flick;
                    flash   = step(_Fill - 0.05, order) * step(order, _Fill);
                }
                else if (style == 9)
                {
                    // DECOMPRESS: a CIRCLE growing out from the centre (aspect-corrected — never a box) with the
                    // old decompressor's dark-first arrival, in the building's OWN colours. Order = radius
                    // blended with luminance (outline/dark fill early, highlights late) and a per-texel hash.
                    // Everything that moves — the sparkling pixels ahead of the frontier, the soft charge
                    // band, the flicker — is gated by _Active (GhostIntake raises it while ore is landing),
                    // so an idle ghost SETTLES to a faint, still body. The frontier wears an era-coloured
                    // contour with a soft era glow band just ahead of it; the printed pixels and their
                    // sparkle flash are the local art colour.
                    float2 rectTexels = _Rect.zw * _MainTex_TexelSize.zw;                          // sprite size in texels
                    float2 aspect = rectTexels / max(rectTexels.x, rectTexels.y);                   // longest side = 1
                    float2 dv = (luv - 0.5) * 2.0 * aspect;
                    float r = length(dv);                                                           // circular radius, 0 centre … ~1.41 corner
                    float ang = atan2(dv.y, dv.x) / 6.2831853 + 0.5;                                // 0..1 around the circle
                    float rN = saturate(r / 1.42);
                    order = saturate(rN * 0.5 + pow(lum, 0.7) * 0.35 + h * 0.15);
                    // the boundary: contour radius follows progress; a soft era glow charges the band just outside it
                    float rCorner = length(aspect);                                                // the sprite's far corner — the ring gets there at 100%
                    float rFront = _Progress * rCorner;
                    float halfTexels = max(rectTexels.x, rectTexels.y) * 0.5;
                    float ringW = 1.5 / max(halfTexels, 1.0) * 1.42;
                    // the ring: a bright line with dashes running round it, a soft glow just inside, a fainter
                    // wake ring trailing behind, all shimmering with noise around the circumference
                    float ringMain = 1.0 - smoothstep(0.0, ringW, abs(r - rFront));
                    float dashes   = 0.55 + 0.45 * sin(ang * 6.2831853 * 14.0 - t * 5.0);
                    float ringWake = (1.0 - smoothstep(0.0, ringW * 1.3, abs(r - (rFront - 0.07)))) * 0.4;
                    float innerGlow = (1.0 - smoothstep(0.0, 0.1, rFront - r)) * step(r, rFront) * 0.35;
                    float nRing    = 0.6 + 0.4 * Noise2(float2(ang * 12.0, t * 0.7));
                    float ring = saturate((ringMain * (0.7 + 0.3 * dashes) + ringWake + innerGlow) * nRing);
                    float charge = (1.0 - smoothstep(0.0, 0.28, r - rFront)) * step(rFront, r);   // fades out ahead of the ring
                    charge *= _Active * (0.7 + 0.3 * sin(t * 6.0 - r * 12.0));                    // breathes only while ore lands
                    float printing = step(0.001, _Progress) * step(_Progress, 0.999);
                    // the static is a PREMONITION: a pixel flickers only when its turn is near (order just
                    // ahead of the frontier), drawn as a translucent preview that thickens as it gets nearer,
                    // and it fades out fast beyond the purple ring — the far corners stay quiet
                    float band  = 0.12;
                    float ahead = saturate((order - _Fill) / band);                                // 0 at the frontier … 1 far ahead
                    float spatial = 1.0 - smoothstep(0.0, 0.22, r - rFront);                       // outside the ring: gone quickly
                    float pSoon = pow(1.0 - ahead, 2.0);                                           // due soon → flickers often
                    float draw  = Hash2(texel + floor(t * 10.0) * 7.0);
                    float gate  = step(1.0 - _Active, Hash2(texel * 3.1 + floor(t * 10.0)));     // fewer as activity fades
                    float sparkle = step(_Fill, order) * step(ahead, 0.999) * step(draw, pSoon) * gate;
                    preview = sparkle * spatial * (0.3 + 0.7 * pSoon);
                    float flick = lerp(1.0, 0.85 + 0.15 * Hash1(floor(t * 14.0)), _Active);
                    float3 local = lerp(tex.rgb, float3(1.0, 1.0, 1.0), 0.3);                   // the art's colour, lifted
                    bodyA   = _Blueprint.a * 0.22 * flick + charge * 0.35 * printing;
                    bodyRGB = lerp(_Blueprint.rgb, _Outline.rgb, saturate(charge * 1.5));
                    edgeRGB = lerp(tex.rgb, float3(1.0, 1.0, 1.0), 0.15);                        // contour at rest = local colour
                    outlineA = _Outline.a * 0.28;                                                  // faint at rest
                    glowRGB = local;
                    headK   = 0.0;                                                                 // the era ring is the head here
                    flash   = step(_Fill - 0.04, order) * step(order, _Fill) * _Active * 0.6;      // settled pixels flash once
                    contour = ring * printing * (0.25 + 0.75 * _Active);
                    // as the ring sweeps past the silhouette, the outline it has crossed lights up era-coloured —
                    // so the boundary stays visible on wide/tall sprites until the very last pixel
                    float passed = 1.0 - smoothstep(rFront - 0.06, rFront, r);
                    contour = max(contour, edge * passed * printing * (0.3 + 0.5 * _Active));
                }
                else
                {
                    // SCHEMATIC: dark silhouette, diagonal hatch, slow light sweep; bottom-up raster print
                    order = saturate(luv.y * (1.0 - _Jitter) + h * _Jitter);
                    float hatch = step(frac((texel.x + texel.y) / max(_HatchPeriod, 1.0)), 1.0 / max(_HatchPeriod, 1.0));
                    float sweepPos = frac(t * _SweepSpeed);
                    float sweep = _Sweep * (1.0 - smoothstep(0.0, 0.1, abs(luv.y - sweepPos)));
                    bodyA   = _Blueprint.a + hatch * _Hatch + sweep * 0.5;
                    bodyRGB = _Blueprint.rgb + sweep * 0.25;
                }

                float shown = step(order, _Fill);                                   // 1 = printed

                // ---- schematic pixel: body (dimmed by _Brightness) or outline
                float3 schRGB = lerp(bodyRGB, edgeRGB, edge);
                float  schA   = lerp(saturate(bodyA * _Brightness), outlineA, edge) * tex.a;
                half4  schematic = half4(schRGB, schA);

                // ---- composite: PRINTED pixels are the art tinted toward the era/ore colour (structure kept
                // through luminance) — the true colours only pop when the building completes and its own
                // material returns. Only the printed art is tinted: the body, the contour, the era ring and
                // the flash/glow keep their own colours (no era-on-era).
                half4 art = tex * IN.color;
                float3 eraLit = _Outline.rgb * (0.3 + 0.7 * lum);
                art.rgb = lerp(art.rgb, eraLit, _Tint);
                // printed-pixel SHEEN, the ember's-edge shimmer kept inside the sprite: a slow diagonal gloss
                // band, and where it passes the texels refract a fraction of a texel (with a whisper of
                // chromatic split) and lift in brightness — the print reads as wet/glassy until it completes
                // RADIAL sheen: gloss rings pulsing out from the centre (aspect-corrected, like the print) plus
                // a slow rotating spoke, each broken up by drifting Perlin-style noise so it never reads as
                // one clean sweep
                float2 sheenRect = _Rect.zw * _MainTex_TexelSize.zw;
                float2 sheenAsp = sheenRect / max(sheenRect.x, sheenRect.y);
                float2 sd = (luv - 0.5) * 2.0 * sheenAsp;
                float  sr = length(sd);
                float  sa = atan2(sd.y, sd.x) / 6.2831853 + 0.5;                                 // 0..1 around
                float2 nUv = luv * (_SheenTiles * 3.0) + t * float2(0.17, -0.11);
                float n  = Fbm2(nUv);
                float b1 = GlossBand(sr * _SheenTiles - t * _SheenSpeed);                          // rings outward
                float b2 = GlossBand(sr * _SheenTiles * 0.6 + t * _SheenSpeed * 0.45 + 0.5);       // a slower ring drifting inward
                float b3 = GlossBand(sa + t * _SheenSpeed * 0.35) * saturate(sr * 3.0);           // rotating spoke, fading at the centre
                float gloss = saturate(max(b1, max(b2, b3)) * (0.3 + 0.7 * n) + n * n * 0.18) * _Sheen;
                // refraction direction comes off the noise field too (a slow, curling wobble)
                float2 wob = (float2(Fbm2(nUv + 3.3), Fbm2(nUv - 5.9)) - 0.5) * 2.0 * _MainTex_TexelSize.xy * _SheenWobble * gloss;
                float2 lo = _Rect.xy + _MainTex_TexelSize.xy * 0.5, hi = _Rect.xy + _Rect.zw - _MainTex_TexelSize.xy * 0.5;
                float2 uvw = clamp(IN.uv + wob, lo, hi);
                float2 chroma = float2(_MainTex_TexelSize.x * 0.35 * gloss, 0.0);
                float  rW = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, clamp(uvw + chroma, lo, hi)).r;
                half4  texW = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvw);
                float  bW = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, clamp(uvw - chroma, lo, hi)).b;
                float3 refracted = float3(rW, texW.g, bW) * IN.color.rgb;
                float  lumW = dot(refracted, float3(0.299, 0.587, 0.114));
                refracted = lerp(refracted, _Outline.rgb * (0.3 + 0.7 * lumW), _Tint);
                art.rgb = lerp(art.rgb, refracted, saturate(gloss * 1.5));
                art.rgb += gloss * 0.35 * (0.4 + 0.6 * lum);
                half4 c = lerp(schematic, art, shown);
                c.rgb = lerp(c.rgb, art.rgb, preview);                                             // premonition of the pixel
                c.a   = max(c.a, preview * tex.a);

                // fresh-print flash (styles that use it) and the print head band while printing
                float midPrint = step(0.001, _Fill) * step(_Fill, 0.999);
                float head = midPrint * step(_Fill, order) * step(order, _Fill + _HeadWidth);
                c.rgb = lerp(c.rgb, glowRGB, max(head * headK, flash * shown * 0.7));
                c.a   = max(c.a, head * headK * _Outline.a * tex.a);
                // the era contour lies over everything on the boundary
                c.rgb = lerp(c.rgb, _Outline.rgb, contour);
                c.a   = max(c.a, contour * _Outline.a * tex.a);

                c.rgb *= c.a;                                                        // premultiplied out
                return c;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
