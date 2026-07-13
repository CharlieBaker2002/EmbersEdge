using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public class OrbManager : MonoBehaviour
{
    public static List<OrbScript> allOrbs;
    public Transform CS;
    float dist;
    float[] speeds = new float[] { 1f, 1.2f, 0.5f, 1.6f };
    float[] disperseSpeeds = new float[] { 1f, 1.2f, 0.5f, 1.6f };
    Vector2 dir;
    public static float distortion = 1f;

    // ---- wild-orb lifetime cohorts ----
    // Base wild orbs don't rot on a clock — each lives until the NEXT boundary of the kind
    // that minted it: day-born → the next day tick, wave-born → the next wave start, dropped
    // by a returning bag drone → the next return from the dungeon. Dungeon wild orbs keep the
    // classic 75s rot AND die the moment the player teleports home.
    static int dayN, waveN, returnN;
    bool lastEeactive;
    System.Action newDay;
    System.Action<bool> teleport;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCohorts()   // no-domain-reload: statics survive play-stop
    {
        dayN = 0;
        waveN = 0;
        returnN = 0;
    }

    /// <summary>The current counter for a cohort kind — what a fresh orb of that kind stamps.</summary>
    public static int CohortStamp(int kind)
        => kind == 0 ? dayN : kind == 1 ? waveN : kind == 2 ? returnN : 0;

    private void Start()
    {
        if (RefreshManager.i.CASUALNOTREALTIME)
        {
            speeds = new[] { 5f, 5f, 5f, 5f };
        }
        lastEeactive = SpawnManager.eeactive;
        StartCoroutine(HookCohortEvents());
    }

    IEnumerator HookCohortEvents()
    {
        while (SpawnManager.instance == null || PortalScript.i == null) yield return null;
        newDay = () => { dayN++; SweepWildCohort(0, dayN); };
        SpawnManager.instance.OnNewDay += newDay;
        teleport = nowInDungeon =>
        {
            if (nowInDungeon) return;
            // home again: dungeon loot that wasn't hauled is gone, and the PREVIOUS return's
            // drone-spill pile expires — this return mints the next cohort
            returnN++;
            SweepWildCohort(2, returnN);
            KillDungeonWild();
        };
        PortalScript.i.onTeleport += teleport;
    }

    void OnDestroy()
    {
        if (SpawnManager.instance != null && newDay != null) SpawnManager.instance.OnNewDay -= newDay;
        if (PortalScript.i != null && teleport != null) PortalScript.i.onTeleport -= teleport;
    }

    /// <summary>Retire every wild base orb of a cohort minted before the boundary that just
    /// passed. Backwards walk — ReturnToPool's OnDisable removes from allOrbs in place.</summary>
    static void SweepWildCohort(int kind, int stampBelow)
    {
        if (allOrbs == null) return;
        for (int k = allOrbs.Count - 1; k >= 0; k--)
        {
            var o = allOrbs[k];
            if (o == null || !o.isActiveAndEnabled || o.state != OrbScript.OrbState.wild) continue;
            if (o.wildKind != kind || o.wildStamp >= stampBelow) continue;
            o.ReturnToPool();
        }
    }

    /// <summary>Teleport home: uncollected dungeon wild orbs die with the run (unstamped ones
    /// included — the position is the truth here, not the cohort).</summary>
    static void KillDungeonWild()
    {
        if (allOrbs == null) return;
        for (int k = allOrbs.Count - 1; k >= 0; k--)
        {
            var o = allOrbs[k];
            if (o == null || !o.isActiveAndEnabled || o.state != OrbScript.OrbState.wild) continue;
            if (!o.transform.InDungeon()) continue;
            o.ReturnToPool();
        }
    }

    void Update()
    {
        //float p = Time.time % 40f;
        //Debug.Log(p + " " + distortion);
        //if(p < 17.5f)
        //{
        //    distortion = Mathf.Lerp(distortion, 2.5f, 0.0005f * Time.deltaTime * p * p);
        //}
        //else if(p < 20)
        //{
        //    distortion = Mathf.Lerp(distortion, 3.5f, Time.deltaTime);
        //}
        //else if(p > 22.5)
        //{
        //    distortion = Mathf.Lerp(distortion, 1f, 0.003f * Time.deltaTime * Mathf.Pow(42.5f - p,2));
        //}

        // wave start (no event exists — eeactive is the truth): the previous wave's cohort expires
        if (SpawnManager.eeactive != lastEeactive)
        {
            lastEeactive = SpawnManager.eeactive;
            if (lastEeactive) { waveN++; SweepWildCohort(1, waveN); }
        }

        for(int i = 0; i < OrbScript.tot; i++)
        {
            if(i >= allOrbs.Count) { break; }
            OrbScript o = allOrbs[i];
            if(o == null) { continue; }
            if (!o.isActiveAndEnabled) { continue; }
            if (OrbScript.tot > 1752 * 3 * (0.01f + SetM.OrbQuality))
            {
                if (Random.Range(0, 2) == 0)
                {
                    continue;   // over budget: skip this orb's tick, not the whole loop
                }
            }
            Transform tr = o.tr;
            switch (o.state)
            {
                case OrbScript.OrbState.wild:
                    if (o.wildKind < 0) o.StampWildCohort();   // first wild tick — position settled
                    o.timeLeft -= Time.deltaTime;
                    if (o.timeLeft <= 0f)
                    {
                        // Only DUNGEON orbs rot on the clock; base orbs wait for their cohort's
                        // boundary (day tick / wave start / dungeon return — see the sweeps).
                        if (o.wildKind == 3)
                        {
                            // Release deactivates the orb, whose OnDisable removes it from allOrbs —
                            // the list shifts left, so step back to not skip the orb that slid in.
                            o.ReturnToPool();
                            i--;
                            continue;
                        }
                        o.timeLeft = 0f;
                    }
                    if (o.timeLeft > 74f)
                    {
                        tr.position += (o.rot+90) * (o.timeLeft - 74f) * 2f * disperseSpeeds[o.orbType] * Time.deltaTime * new Vector3(Mathf.Sin(o.theta),Mathf.Cos(o.theta))/360f * Random.Range(1.5f,2f);
                        // Scattered into the ember's edge — shove the orb back inside the boundary
                        // and end its scatter so it can't wander back out. Base side only (the
                        // dungeon dimension has its own walls and no map boundary). Code-based test,
                        // then one spline query only for the rare offender.
                        Vector3 wp = tr.position;
                        if (PathZone.AtBase(wp) && !MapManager.InsideBoundsWithClearance(wp, 0.2f))
                        {
                            Vector2 close = MapManager.i.ProximityData(wp).Item1;
                            // inward = away from the nearest boundary point when already inside the
                            // poly (just under the line), toward it when fully outside
                            Vector2 inward = MapManager.InsideBoundsWithClearance(wp, 0f)
                                ? (Vector2)wp - close : close - (Vector2)wp;
                            // on-the-line degenerate case: fall back to "toward the base centre"
                            inward = inward.sqrMagnitude > 1e-6f ? inward.normalized : -close.normalized;
                            Vector2 fixedPos = close + inward * 0.35f;
                            tr.position = new Vector3(fixedPos.x, fixedPos.y, wp.z);
                            o.timeLeft = 74f;
                        }
                    }
                    else
                    {
                        Vector3 pos = tr.position;
                        dir = (Vector2)(CS.position - pos);
                        if (OrbScript.canAttract[o.orbType])
                        {
                            dist = dir.sqrMagnitude;
                            if (dist < 0.2f)
                            {
                                PlayerCollide(o);
                                continue;
                            }
                            if (dist < 16f + o.orbType)   // wider collection radius (~4 units, was ~2.2)
                            {
                                o.chaseT += Time.deltaTime;
                                // Spring-like pull: base speed PLUS a term that grows with distance, so
                                // distant orbs rush in instead of crawling. (The old 1/dist² falloff made
                                // far orbs the slowest — the opposite of what we want.) On top of that, a
                                // ramp that builds the LONGER an orb has been chasing and bites hardest up
                                // CLOSE, so a near orb doesn't dawdle — it accelerates in the last stretch.
                                float d = Mathf.Sqrt(dist);
                                float ramp = Mathf.Min(o.chaseT, 3f) / (0.5f + d);
                                // The whole pull compounds the longer an orb has been chasing: a fresh orb
                                // starts at 1× and winds up to ~7× after ~5s, so orbs that keep failing to
                                // reach you accelerate hard and quickly close the gap instead of trailing.
                                float chaseBoost = 1f + 1.2f * Mathf.Min(o.chaseT, 5f);
                                tr.position = pos + chaseBoost * disperseSpeeds[o.orbType] * Time.deltaTime * (2f + 2f * d + ramp) * (Vector3)(dir / d);
                            }
                            else o.chaseT = 0f;   // drifted out of range — reset the chase ramp
                        }
                        else o.chaseT = 0f;
                    }
                    break;
                case OrbScript.OrbState.collect:
                    if (OrbScript.canAttract[o.orbType])
                    {
                        Vector3 pos = tr.position;
                        dir = CS.position - pos;
                        dist = dir.sqrMagnitude;
                        if (dist < Mathf.Pow(10f * Time.deltaTime, 2))
                        {
                            PlayerCollide(o);
                            continue;
                        }
                        tr.position = pos + 7.5f * Time.deltaTime * (Vector3)dir.normalized;
                    }
                    break;
                case OrbScript.OrbState.decelerate:
                    tr.localPosition = Vector2.Lerp(tr.localPosition, Vector3.zero, 0.6f * speeds[o.orbType] * Time.deltaTime);
                    continue;
                case OrbScript.OrbState.accelerate:
                    tr.Translate(speeds[o.orbType] * Time.deltaTime * 3 * -tr.localPosition.normalized);
                    continue;
                case OrbScript.OrbState.harvest:
                    if (o.hovTimer > 0f)
                    {
                        o.hovTimer -= Time.deltaTime;
                        if(o.hovTimer <= 0f)
                        {
                            tr.localPosition = Vector2.zero;
                            o.hovTimer = -1f;
                        }
                        tr.localPosition = Vector2.Lerp(tr.localPosition, Vector2.zero, 3f * Time.deltaTime * (2f-o.hovTimer));

                    }
                    else
                    {
                        tr.localPosition = Vector2.Lerp(tr.localPosition, Random.insideUnitCircle * 0.2f ,  Time.deltaTime);
                    }
                    continue;
                case OrbScript.OrbState.hover:
                    o.hovTimer -= Time.deltaTime;
                    if (o.hovTimer > 0f)
                    {
                        tr.localPosition = Vector3.Lerp(tr.localPosition, new Vector2(distortion * (-0.5f + 1f * Mathf.PerlinNoise(Mathf.Sin(o.theta + 0.8f*Time.time),0.35f*Time.time)), -0.5f + Mathf.PerlinNoise(Mathf.Cos(o.theta+ 0.8f*Time.time), 0.35f*Time.time)).Rotated(o.rot), 4f * Time.deltaTime);
                    }
                    else if(o.hovTimer < 0f)
                    {
                        o.theta = Random.Range(0f, 2 * Mathf.PI);
                        o.hovTimer = Random.Range(2f, 4f);
                    }
                    break;
                case OrbScript.OrbState.hoverstore:
                    o.hovTimer -= Time.deltaTime;
                    if (o.hovTimer > 0f)
                    {
                        dir = new Vector2(distortion * (-0.5f + 1f * Mathf.PerlinNoise(Mathf.Sin(o.theta + 0.8f * Time.time), 0.35f * Time.time)), -0.5f + Mathf.PerlinNoise(Mathf.Cos(o.theta + 0.8f * Time.time), 0.35f * Time.time)).Rotated(o.rot);
                        tr.localPosition = Vector3.Lerp(tr.localPosition, new Vector3(dir.x*1.75f,dir.y*0.75f, 0f) , 4f * Time.deltaTime);
                    }
                    else if (o.hovTimer < 0f)
                    {
                        o.theta = Random.Range(0f, 2 * Mathf.PI);
                        o.hovTimer = Random.Range(2f, 4f);
                    }
                    break;
                case OrbScript.OrbState.deposit:
                    o.depT += Time.deltaTime / o.depDur;
                    float t = Mathf.Clamp01(o.depT);
                    float e = t * t * (3f - 2f * t);                       // smoothstep ease-in-out
                    Vector3 s0 = o.depStart;                              // start (local to the pylon)
                    // Straight eased travel along start -> pylon(0), plus a perpendicular wiggle. The
                    // two harmonics are both zero at the ends and each crosses the axis, so the sideways
                    // wander is balanced: a straight beam that snakes with random curves on the way in.
                    Vector3 line = s0 * (1f - e);
                    Vector3 perp = new Vector3(-s0.y, s0.x, 0f).normalized;
                    float wig = o.depWig1 * Mathf.Sin(2f * Mathf.PI * t) + o.depWig2 * Mathf.Sin(3f * Mathf.PI * t);
                    tr.localPosition = line + perp * wig;
                    tr.localScale = Vector3.one * (1f + 0.28f * Mathf.Sin(t * Mathf.PI));  // swell, settle
                    if (t >= 1f) { tr.localPosition = Vector3.zero; tr.localScale = Vector3.one; }
                    continue;
            }
        }
    }

    void PlayerCollide(OrbScript o)
    {  
        if (ResourceManager.instance.HasRoom(o.orbType))
        {
            o.state = OrbScript.OrbState.follow;
            ResourceManager.instance.heldOrbs.Add(o);
            o.gameObject.SetActive(false);
            // Auto-bank, but ONLY at base — never ship resources back to base while diving. In the
            // dungeon the orb stays held on the player (deposited on portal return). At base, anything
            // the bank has room for flows in right away; only overflow stays held. DropResources
            // rebuilds heldOrbs and re-opens attraction, so collecting never stalls with space free.
            if (PortalScript.i == null || !PortalScript.i.inDungeon)
            {
                ResourceManager.instance.DropResources(o.orbType);
            }
        }
        else
        {
            o.ReturnToPool();
        }
    }

    public static IEnumerator LerpDistortion(float t, float wait = 0f)
    {
        if(wait > 0f)
        {
            yield return new WaitForSeconds(wait);
        }
        for (float i = 0f; i <= 5f; i+= Time.deltaTime)
        {
            distortion = Mathf.Lerp(distortion, t, i * Time.deltaTime);
            yield return null;
        }
    }
    //era, 
}


