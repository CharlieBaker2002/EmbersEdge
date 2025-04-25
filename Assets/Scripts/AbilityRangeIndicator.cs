using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class AbilityRangeIndicator : MonoBehaviour
{
    public static SpriteRenderer[][] srs = new SpriteRenderer[][] { new SpriteRenderer[] { }, new SpriteRenderer[] { }, new SpriteRenderer[] { } };
    public static bool[] turnOns = new bool[3] { false, false, false };
    public bool manager = false;

    public enum AimMode { none, mouse, forward }; //forward is for indicators that have weird rotational parents (e.g. shruiken shield which maintains rotation after casting).
    public AimMode mode = AimMode.none; 
    public SpriteRenderer render;

    System.Action<InputAction.CallbackContext>[] dels;

    private Vector2 initLocal;

    private void Start()
    {
        if (manager)
        {
            dels = new System.Action<InputAction.CallbackContext>[]
            {
                ctx => turnOns[0] = true,
                ctx => turnOns[1] = true,
                ctx => turnOns[2] = true,
                ctx => turnOns[0] = false,
                ctx => turnOns[1] = false,
                ctx => turnOns[2] = false,
            };
            IM.i.pi.Player.Spell1.started += dels[0];
            IM.i.pi.Player.Spell2.started += dels[1];
            IM.i.pi.Player.Spell3.started += dels[2];   

            IM.i.pi.Player.Spell1.performed += dels[3];
            IM.i.pi.Player.Spell2.performed += dels[4];
            IM.i.pi.Player.Spell3.performed += dels[5];
        }
        else
        {
            initLocal = transform.localPosition;
        }
    }

    private void Update()
    {
        if (manager)
        {
            if (turnOns[0] && CharacterScript.CS.spellCDs[0] < 0f && (ResourceManager.instance.fuel + ResourceManager.instance.energy > CharacterScript.CS.manaCosts[0] || CharacterScript.CS.spellBools[0] == true))
            {
                foreach (SpriteRenderer sr in srs[0])
                {
                    if (sr == null)
                    {
                        continue;
                    }
                    sr.enabled = true;
                }
            }
            else if (turnOns[0] == false)
            {
                foreach (SpriteRenderer sr in srs[0])
                {
                    if (sr == null)
                    {
                        continue;
                    }
                    sr.enabled = false;
                }
            }

            if (turnOns[1] && CharacterScript.CS.spellCDs[1] < 0f && (ResourceManager.instance.fuel + ResourceManager.instance.energy > CharacterScript.CS.manaCosts[1] || CharacterScript.CS.spellBools[1] == true))
            {
                foreach (SpriteRenderer sr in srs[1])
                {
                    if (sr == null)
                    {
                        continue;
                    }
                    sr.enabled = true;
                }

            }
            else if (turnOns[1] == false)
            {
                foreach (SpriteRenderer sr in srs[1])
                {
                    if (sr == null)
                    {
                        continue;
                    }
                    sr.enabled = false;
                }
            }

            if (turnOns[2] && CharacterScript.CS.spellCDs[2] < 0f && (ResourceManager.instance.fuel + ResourceManager.instance.energy > CharacterScript.CS.manaCosts[2] || CharacterScript.CS.spellBools[2] == true))
            {
                foreach (SpriteRenderer sr in srs[2])
                {
                    if (sr == null)
                    {
                        continue;
                    }
                    sr.enabled = true;
                }

            }
            else if (turnOns[2] == false)
            {
                foreach (SpriteRenderer sr in srs[2])
                {
                    if (sr == null)
                    {
                        continue;
                    }
                    sr.enabled = false;
                }
            }
        }

        else
        {
            if (render.enabled)
            {
                if (mode == AimMode.mouse)
                {
                    Vector2 mousePosition = Mouse.current.position.ReadValue();

                    // Convert the mouse position to world coordinates
                    Vector3 targetPosition = CameraScript.i.cam.ScreenToWorldPoint(mousePosition);

                    // Calculate the direction from the current position to the target position
                    Vector3 direction = targetPosition - transform.position;

                    // Calculate the angle in degrees
                    float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;

                    // Rotate the GameObject to face the mouse
                    transform.rotation = Quaternion.Euler(new Vector3(0, 0, angle));
                }
                else if(mode == AimMode.forward)
                {
                    transform.SetPositionAndRotation(transform.parent.position + (Vector3)GS.Rotated(initLocal,GS.CS().rotation.eulerAngles.z), GS.CS().rotation);
                }
            }
        }
    }
       



}
