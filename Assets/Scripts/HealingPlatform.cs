using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

public class HealingPlatform : Building
{
    [Header("References")]
    public GameObject healingOrbPrefab;
    public Collider2D platformCollider;
    public TextMeshPro txt;
    public GameObject mesh;

    [Header("Sprites")]
    public Sprite[] platformSprites = new Sprite[6]; // For platform states (0..5)
    public Sprite[] slotSprites;                     // For Increase/Decrease Cap

    [Header("Orb Settings")]
    public int maxCap = 15;
    private int cap = 15;
    public List<HealOrb> healOrbs = new List<HealOrb>();

    [Header("Timers")]
    private float resourceTimer = 0f;  // For new orb creation cost
    private float healCheckTimer = 0f; // For scanning for heal targets

    // Internal states for resource request
    private bool waitingForResource = false;
    private bool resourceApproved = false;

    // Healing logic
    [Tooltip("Cooldown between attempts to send orbs to a unit.")]
    public float healCooldown = 0.5f; // Half-second between sends

    public override void Start()
    {
        base.Start();

        // Example: Draw circle for visual range
        GS.DrawCircle(GetComponentInChildren<LineRenderer>(true), 7f, 50);

        cap = maxCap;
        UpdateText();

        // Hook into building open/close
        OnOpen += () =>
        {
            mesh.SetActive(true);
            txt.gameObject.SetActive(true);
            UpdateText();
        };
        OnClose += () =>
        {
            mesh.SetActive(false);
            txt.gameObject.SetActive(false);
        };

        // Add "slots" from your original code
        AddSlot(new int[] { 0, 0, 0, 0 }, "Increase Cap", slotSprites[0], 
            false, () => { cap++; UpdateText(); }, false, null, CanAdd);
        AddSlot(new int[] { 0, 0, 0, 0 }, "Decrease Cap", slotSprites[1], 
            false, () => { cap--; UpdateText(); }, false, null, CanSubtract);
    }

    private bool CanAdd()     => (cap + 1 <= maxCap);
    private bool CanSubtract() => (cap - 1 >= 0);

    private void Update()
    {
        resourceTimer -= Time.deltaTime;
        healCheckTimer -= Time.deltaTime;

        // 1) Handle new-orb creation (resource request) if under capacity
        TrySpawnNewOrb();

        // 2) Periodically check for a heal target
        if (healCheckTimer <= 0f)
        {
            healCheckTimer = healCooldown; // Reset
            TryHealUnitInRange();
        }
    }

    /// <summary>
    /// Attempt to request resources and spawn a new orb if under capacity.
    /// </summary>
    private void TrySpawnNewOrb()
    {
        if (healOrbs.Count >= cap) return;
        if (resourceTimer > 0f) return;

        // If we aren't currently waiting for a resource, request a new one
        if (!waitingForResource)
        {
            waitingForResource = true;
            resourceTimer = 10f; // Time until we can request again

            // Request from ResourceManager
            bool requested = ResourceManager.instance.NewTask(
                gameObject,
                new int[] { 0, 1, 0, 0 },
                () => { resourceApproved = true; }
            );

            if (!requested)
            {
                // If the resource request fails
                waitingForResource = false;
            }
        }
        else if (resourceApproved)
        {
            // Resource arrived -> create an orb
            waitingForResource = false;
            resourceApproved = false;

            GameObject orbObj = Instantiate(healingOrbPrefab, transform.position, Quaternion.identity, transform);
            var orb = orbObj.GetComponent<HealOrb>();
            orb.hp  = this;
            orb.rad = Random.Range(0.5f, 1.25f);

            healOrbs.Add(orb);
            UpdateSprite();
        }
    }

    /// <summary>
    /// Looks for a single Unit in our collider that needs healing.
    /// Sends only the required number of orbs to fully heal it (or as many as we have).
    /// </summary>
    private void TryHealUnitInRange()
    {
        // Quick check if we even have orbs
        if (healOrbs.Count == 0) return;

        // Attempt to find something to heal in range
        // (One simple way is to do an Overlap. Or use OnTriggerStay2D from your original code, but here we do an explicit check.)
        var hits = new Collider2D[10];
        Physics2D.OverlapCollider(platformCollider, new ContactFilter2D().NoFilter(), hits);

        // Grab the first valid Unit that has missing HP
        Unit targetUnit = null;
        LifeScript targetLS = null;

        foreach (var col in hits)
        {
            if (!col) continue;
            if (!col.attachedRigidbody) continue;

            if (col.attachedRigidbody.TryGetComponent<Unit>(out var unit))
            {
                if (unit.ls != null && unit.ls.hp < unit.ls.maxHp)
                {
                    targetUnit = unit;
                    targetLS   = unit.ls;
                    break;
                }
            }
        }

        if (targetUnit == null || targetLS == null) return;

        // We found a unit that needs healing. Calculate how many orbs are required.
        float missingHP = targetLS.maxHp - targetLS.hp;
        // Each orb can heal up to 3 (based on your random range 2..3)
        int orbsNeeded = Mathf.CeilToInt(missingHP / 3f);

        // We can only send as many orbs as we actually have
        int orbsToSend = Mathf.Min(orbsNeeded, healOrbs.Count);

        // Send that many orbs
        for (int i = 0; i < orbsToSend; i++)
        {
            // Always pick from the front (or back) of our list
            HealOrb orb = healOrbs[0];
            healOrbs.RemoveAt(0);

            orb.StartChase(targetUnit.transform, targetLS);
        }

        UpdateSprite();
    }

    private void OnDisable()
    {
        mesh.SetActive(false);
        txt.gameObject.SetActive(false);
    }

    /// <summary>
    /// Update the main sprite based on how many orbs are in the platform.
    /// </summary>
    public void UpdateSprite()
    {
        UpdateText();

        int count = healOrbs.Count;
        if (count == 0)            sr.sprite = platformSprites[0];
        else if (count <= 4)       sr.sprite = platformSprites[1];
        else if (count <= 8)       sr.sprite = platformSprites[2];
        else if (count <= 11)      sr.sprite = platformSprites[3];
        else if (count < 15)       sr.sprite = platformSprites[4];
        else /* count == 15 */     sr.sprite = platformSprites[5];
    }

    private void UpdateText()
    {
        txt.text = $"{healOrbs.Count} / {cap}";
    }
}