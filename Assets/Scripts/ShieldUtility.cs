using System.Collections;
using UnityEngine;

public static class ShieldUtility 
{
    public enum ShieldType
    {
        Shield,
        DecayingShield,
        DecayInDecayingShield,
        DecayInShield
    }
    
    public static void Shield(Unit n, float max, float duration = 5f, bool weak = false)
    {
        if (n == null) return;

        int ID = n.CreateShield(max, true, weak); 
        Debug.Log("a");
        RefreshManager.i.QA(() =>
        {
            Debug.Log("a");
            if (n != null){ n.RemoveShield(ID);    Debug.Log("b");}
        }, duration);
    }
    
    public static void DecayingShield(Unit n, float max, float duration = 4f, float decayOutTime = 2f, bool weak = false)
    {
        if (n == null) return;

        int ID = n.CreateShield(max, true, weak); // immediate=true => starts at max
        RefreshManager.i.StartCoroutine(IDS());

        IEnumerator IDS()
        {
            // Our local tracking variable. Since immediate=true, we start at max.
            float shieldVal = max;

            yield return new WaitForSeconds(duration);
            if(ChangeShield(n, ID, 0f)) yield break;
            
            // 2) Ramp from shieldVal -> 0 over decayOutTime
            float startVal = shieldVal;
            float startTime = Time.time;
            float endTime   = startTime + decayOutTime;

            while (Time.time < endTime)
            {
                yield return null;
                if (n == null) yield break; // unit destroyed?

                // Interpolation ratio [0..1]
                float ratio  = (Time.time - startTime) / decayOutTime;
                if (ratio > 1f) ratio = 1f;

                float newVal = Mathf.Lerp(startVal, 0f, ratio);
                float delta  = newVal - shieldVal; 
                
                // Apply the delta
                if (ChangeShield(n, ID, delta)) yield break;

                // Update our local tracking
                shieldVal = newVal;
            }

            // 3) Remove final shield if still around
            if (n != null) n.RemoveShield(ID);
        }
    }
    
    public static void DecayInDecayingShield(Unit n, float max, float decayInTime = 2f, float duration = 4f, float decayOutTime = 2f, bool weak = false)
    {
        if (n == null) return;

        int ID = n.CreateShield(max, false, weak); // immediate=false => starts at 0
        RefreshManager.i.StartCoroutine(IDIDS());

        IEnumerator IDIDS()
        {
            // local tracking starts at 0
            float shieldVal = 0f;
            ChangeShield(n, ID, 0.01f, true);
            // 1) Ramp in: from 0 to max over decayInTime
            {
                float startVal = shieldVal;
                float startTime = Time.time;
                float endTime   = startTime + decayInTime;

                while (Time.time < endTime)
                {
                    yield return null;
                    if (n == null) yield break;

                    float ratio  = (Time.time - startTime) / decayInTime;
                    if (ratio > 1f) ratio = 1f;

                    float newVal = Mathf.Lerp(startVal, max, ratio);
                    float delta  = newVal - shieldVal;

                    if (ChangeShield(n, ID, delta)) yield break;

                    shieldVal = newVal;
                }
            } 
            yield return new WaitForSeconds(duration);
            if(ChangeShield(n, ID, 0f)) yield break;
            
            // 3) Ramp out: from shieldVal -> 0 over decayOutTime
            {
                float startVal = shieldVal;
                float startTime = Time.time;
                float endTime   = startTime + decayOutTime;

                while (Time.time < endTime)
                {
                    yield return null;
                    if (n == null) yield break;

                    float ratio = (Time.time - startTime) / decayOutTime;
                    if (ratio > 1f) ratio = 1f;

                    float newVal = Mathf.Lerp(startVal, 0f, ratio);
                    float delta  = newVal - shieldVal;

                    if (ChangeShield(n, ID, delta)) yield break;

                    shieldVal = newVal;
                }
            }

            // 4) Remove
            if (n != null) n.RemoveShield(ID);
        }
    }
    
    public static void DecayInShield(Unit n, float max, float decayInTime = 2f, float duration = 4f, bool weak = false)
    {
        if (n == null) return;

        int ID = n.CreateShield(max, false, weak); // immediate=false => starts at 0
        RefreshManager.i.StartCoroutine(IDIS());

        IEnumerator IDIS()
        {
            // local tracking
            float shieldVal = 0f;
            ChangeShield(n, ID, 0.01f, true);
            // 1) Ramp in: 0 -> max over decayInTime
            {
                float startVal = shieldVal;
                float startTime = Time.time;
                float endTime   = startTime + decayInTime;

                while (Time.time < endTime)
                {
                    yield return null;
                    if (n == null) yield break;

                    float ratio  = (Time.time - startTime) / decayInTime;
                    if (ratio > 1f) ratio = 1f;

                    float newVal = Mathf.Lerp(startVal, max, ratio);
                    float delta  = newVal - shieldVal;

                    if (ChangeShield(n, ID, delta)) yield break;
                    shieldVal = newVal;
                }
            }

            yield return new WaitForSeconds(duration);
            
            // 3) Remove
            if (n != null) n.RemoveShield(ID);
        }
    }
    
    private static bool ChangeShield(Unit n, int ID, float delta, bool ignoreProblem = false)
    {
        if (n == null) return true; // no unit => we must stop
        
        float newShieldValue = n.ModifyShieldStrength(ID, delta);
        if (ignoreProblem) return false;
        if (delta > 0f)
        {
            if (newShieldValue <= delta)
            {
                n.RemoveShield(ID);
                return true;
            }
        }
        else if (newShieldValue <= 0f)
        {
            n.RemoveShield(ID);
            return true;
        }
        return false;
    }
}