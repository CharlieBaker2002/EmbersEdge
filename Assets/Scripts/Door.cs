using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Door : MonoBehaviour
{
    //wall layer
    public Room room1;
    public Room room2;
    [HideInInspector]
    public Collider2D col;
    public string keyName = "";
    public Sprite openDoor;
    public Sprite closeDoor;
    public Sprite[] wallSprites;
    [HideInInspector]
    public SpriteRenderer sr;
    [HideInInspector]
    bool beingDestroyed = false;


    public virtual void Awake()
    {
        col = GetComponent<Collider2D>();
        sr = GetComponent<SpriteRenderer>();
    }

    private void OnTriggerExit2D(Collider2D collision) //collider must mediate between two rooms
    {
        if (room2 != null)
        {
            if(collision.name == "Character")
            {
                bool o1 = room1.transform.parent.gameObject.activeInHierarchy;
                bool o2 = room2.transform.parent.gameObject.activeInHierarchy;
                room1.transform.parent.gameObject.SetActive(true);
                room2.transform.parent.gameObject.SetActive(true);
                if (CharacterScript.CS.miniMarker.bounds.Intersects(room2.col.bounds))
                {
                    room2.transform.parent.gameObject.SetActive(true);
                    room2.OnEnter();
                    room1.transform.parent.gameObject.SetActive(o1);
                }
                else if(CharacterScript.CS.miniMarker.bounds.Intersects(room1.col.bounds))
                {
                    room1.transform.parent.gameObject.SetActive(true);
                    room1.OnEnter();
                    room2.transform.parent.gameObject.SetActive(o2);
                }
                else
                {
                    if (room1.hasCharacter > room2.hasCharacter)
                    {
                        room1.transform.parent.gameObject.SetActive(true);
                        room1.OnEnter();
                    }
                    else
                    {
                        room2.transform.parent.gameObject.SetActive(true);
                        room2.OnEnter();
                    }
                }

            }
        }
    }

    public void OnTriggerEnter2D(Collider2D collision)
    {
        if(room1 != DM.i.initR[GS.era])
        {
            if (!beingDestroyed)
            {
                if (room2 == null)
                {
                    if (collision.TryGetComponent<IRoom>(out var ir))
                    {
                        Room r = ir.GetRoom();
                        if (r != room1)
                        {
                            room2 = r;
                            r.doors.Add(this);
                        }
                    }
                }
                if (collision.TryGetComponent<Door>(out var d))
                {
                    if(d.room1 != DM.i.initR[GS.era])
                    {
                        if (d.transform.rotation == transform.rotation)
                        {
                            if (d.room2 != null && d.room2.doors.Contains(this) == false)
                            {
                                d.room2.doors.Add(this);
                                d.room2.doors.Remove(d);
                            }
                            else if (d.room1.doors.Contains(this) == false)
                            {
                                d.room1.doors.Add(this);
                                d.room1.doors.Remove(d);
                            }
                            d.beingDestroyed = true;
                            Destroy(d.gameObject);
                        }
                        else
                        {
                            if (d.room2 != null)
                            {
                                d.room2.doors.Remove(d);
                            }
                            d.room1.doors.Remove(d);
                            d.beingDestroyed = true;
                            beingDestroyed = true;
                            d.CloseDoor();
                            CloseDoor();
                            room1.doors.Remove(this);
                            if (room2 != null)
                            {
                                room2.doors.Remove(this);
                            }
                            Destroy(d);
                            Destroy(this);
                            d.sr.sprite = d.WallSprite();
                            sr.sprite = WallSprite();
                        }
                    }
                    else
                    {
                        if(d.room2 == null)
                        {
                            d.room2 = room1;
                        }
                        room1.doors.Remove(this);
                        room1.doors.Add(d);
                        if (room2 != null)
                        {
                            if (room2.doors.Contains(this))
                            {
                                room2.doors.Remove(this);
                            }
                            beingDestroyed = true;
                            Destroy(gameObject);
                        }
                    }
                }
            }
        }
    }

    public virtual void CloseDoor()
    {
        if (col != null)
        {
            col.isTrigger = false;
            sr.sprite = closeDoor;
        }
    }

    public virtual void OpenDoor(List<string> keys)
    {
        if (sr != null)
        {
            if (keyName == string.Empty || keys.Contains(keyName))
            {
                sr.sprite = openDoor;
                col.isTrigger = true;
            }
        }
    }

    public Sprite WallSprite()
    {
        int i = Random.Range(0, wallSprites.Length);
        return wallSprites[i];
    }

    public void BecomeWall()
    {
        sr.sprite = WallSprite();
        if (room2 != null)
        {
            room2.doors.Remove(this);
        }
        room1.doors.Remove(this);
        Destroy(this);
    }
}
