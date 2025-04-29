using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Linq;
using UnityEngine.InputSystem;
using Image = UnityEngine.UI.Image;

public class UIManager : MonoBehaviour
{
    public static UIManager i;
    public Image background;
    public RectTransform canvas;
    public RectTransform directors;
    public Transform dmgTextParent;
    public float scaler;
    public float lifeTime = 1.5f;
    public GameObject Text;
    public ColourSO colSO;
    public TextMeshProUGUI title;
    public RectTransform[] positions;

    public GameObject baseTile;
    public GameObject empty;
    public TextMeshPro numText;
    public Transform buildingsUI;
    public GameObject pauseUI;
    private int pauseID = 0;
    public bool canPauseMenu = true;

    public RectTransform lootUI;
    public Transform mechaUI;
    public TextMeshProUGUI dayText;

    public List<RawImage> rawImages = new();
    [SerializeField] Camera telecam; //attached to mainCore.
    [SerializeField] Transform teleTrans;
    public enum TeleMode { Core, Base, Baron, Changing};
    public TeleMode telemode = TeleMode.Core;

    [SerializeField] Image[] backgroundUIImgs;
    [SerializeField] Image teleimg;
    [SerializeField] Sprite[] telesprites;

    [SerializeField] Image[] lootImgs;
    public CanvasGroup cg;

    [SerializeField] private GameObject[] energySliders;
    [SerializeField] private RectTransform[] otherSliders;

    private System.Action<bool> pauseOnTabDel;

    public static Dictionary< string,GameObject> keyGuides;

    [SerializeField] string[] keyOverrides = { };
    [SerializeField] private Sprite[] keyOverrideImages;

    public Action<InputAction.CallbackContext> escapeDel;

    public Hint[] hints;

    public Sprite[] partClassifierImages;

    public static bool partOptionOpened = false; //from vessels
    public static Vessel currentVessel;
    public static bool noInCanvas = false;

    public string[] letters;
    public Sprite[] keysprites;
    private void Awake()
    {
        i = this;
        noInCanvas = false;
        pauseOnTabDel = PauseOnTab;
        #if !UNITY_EDITOR
        Application.focusChanged += pauseOnTabDel;
        #endif
    }

    public void Hint(string nam)
    {
        nam = nam.ToLower();
        foreach (Hint h in hints)
        {
            if (h.name.ToLower() == nam)
            {
                if (h.shown) return;
                if(h.doNotDisplay)   return;
                h.timeID = SpawnManager.instance.NewTS(0f, Mathf.Infinity);
                h.gameObject.SetActive(true);
                return;
            }
        }
    }

    void PauseOnTab(bool on)
    {
        if (!on)
        {
            SpawnManager.instance.CancelTS(pauseID);
            pauseID = SpawnManager.instance.NewTS(0f, Mathf.Infinity);
            pauseUI.SetActive(true);
            Debug.Log("heklo world");
        }
        else
        {
            SpawnManager.instance.CancelTS(pauseID);
        }
    }

    private void OnDestroy()
    {
        if (!SetM.quit)
        {
            Application.focusChanged -= pauseOnTabDel;
        }
    }

    public void UpdateDayText(int day)
    {
        LeanTween.cancel(dayText.gameObject);
        LeanTween.LeanTMPAlpha(dayText, 0f, day == 1 ? 0.1f : 1f).setOnComplete(() => { dayText.color = new Color(1, 1, 1, 0); LeanTween.LeanTMPAlpha(dayText, 1f, 4f).setEaseOutExpo().setOnComplete(() => dayText.LeanTMPColor(new Color(0.85f,0.85f,0.85f,0.9f),1f)); dayText.text = GS.ToRoman(day); });
        LeanTween.scale(dayText.gameObject, new Vector3(1f, 1.5f, 1), 1.5f).setDelay(2f).setLoopPingPong(1);
        LeanTween.moveLocalY(dayText.gameObject,dayText.transform.localPosition.y -25f, 1.5f).setDelay(2f).setLoopPingPong(1);
    }


    private void Start()
    {
        if (RefreshManager.i.STARTSEQUENCE)
        {
            cg.alpha = 0f;
        }
        escapeDel = _ =>
        {
            if (RefreshManager.i.ARENAMODE)
            {
                CheckPauseMenu();
                return;
            }
            if (partOptionOpened)
            {
                GoBackToPrevVesselOptions();
                return;
            }
            CheckPauseMenu();
            CloseAllUIs();
        }; 
        IM.i.pi.Player.Escape.performed += escapeDel;
        if (RefreshManager.i.ARENAMODE)
        {
            IM.i.pi.Player.Escape.Enable();
        }
        GS.OnNewEra += (ctx) => { Color c = Color.Lerp(GS.ColFromEra(), Color.clear, 0.625f); foreach (Image img in backgroundUIImgs) { img.color = c; } teleimg.sprite = telesprites[GS.era - 1]; ResetRaws(); };
    }

    private void GoBackToPrevVesselOptions()
    {
        if(currentVessel == null){ return;}
        currentVessel.GoBackButton();
        partOptionOpened = false;
    }

    public void DamageText(float dmg, int typ, Vector2 pos)
    {
        StartCoroutine(DamageTextI(Mathf.Abs(dmg), typ, pos));
    }

    IEnumerator DamageTextI(float dmg, int typ, Vector2 pos)
    {
        var text = Instantiate(Text, pos, Quaternion.identity, dmgTextParent);
        var txt = text.GetComponent<TextMeshPro>();
        if (dmg >= 5f)
        {
            txt.text = dmg.ToString("F0");
        }
        else if (dmg > 0f)
        {
            if (Mathf.RoundToInt(dmg) == dmg)
            {
                txt.text = dmg.ToString("F0");
            }
            else if(dmg>= 0.5f)
            {
                txt.text = dmg.ToString("F1");
            }
            else
            {
                txt.text = dmg.ToString("F2");
            }
        }
        else
        {
            Destroy(text);
            yield break;
        }
        float timer = lifeTime / 2f;
        float dScaler = 1 + scaler * Mathf.Log(Mathf.Max(1, dmg), 2);

        Gradient gradient = new Gradient();
        GradientColorKey[] keys = new GradientColorKey[2];
        keys[0].color = Color.black;
        keys[0].time = 0;
        if (typ < 0)
        {
            keys[1].color = GS.ColFromEra() * 1.25f;
        }
        else
        {
            keys[1].color = colSO.cols[typ];
        }
        keys[1].time = timer;
        var alphaKey = new GradientAlphaKey[2];
        alphaKey[0].alpha = 0f;
        alphaKey[0].time = 0.0f;
        alphaKey[1].time = 1f;
        
        txt.fontStyle = FontStyles.Italic;
        txt.outlineColor = colSO.Level1;
        alphaKey[1].alpha =
        timer /= 1.5f;
           
        gradient.SetKeys(keys, alphaKey);
        while (timer > 0f)
        {
            txt.color = gradient.Evaluate(((lifeTime / 2) - timer) / (lifeTime / 2));
            timer -= Time.deltaTime;
            text.transform.localScale += dScaler * Time.deltaTime * Vector3.one;
            yield return null;
        }

        txt.color = typ switch
        {
            -1 => new Color(0.9f,0.25f,0f),
            -2 => Color.gray,
            -3 => Color.white,
            -4 => Color.green,
            -5 => Color.blue,
            -6 => new Color(0.891f,0.0275f,0.3f),
            _ => GS.ColFromEra()
        };

        timer = lifeTime / 1.5f;
        while (timer > 0f)
        {
            timer -= Time.deltaTime;
            text.transform.localScale -= Vector3.one * (dScaler * Time.deltaTime);
            dScaler += Time.deltaTime;
            yield return null;
        }
        Destroy(text);
    }

    public static void CloseAllUIs()
    {
        if(i.pauseUI.activeInHierarchy)
        {
            SpawnManager.instance.CancelTS(i.pauseID);
            i.pauseUI.SetActive(false);
            IM.i.StopControllerMoving();
        }
        if (BM.i.UI.activeInHierarchy)
        {
            BM.i.CloseUIs();
        }
        if (CharacterScript.CS.groupUIParent.activeInHierarchy)
        {
            CharacterScript.CS.AltGroupUI();
        }
        Building.CloseBuildingUIs();
    }

    public void CheckPauseMenu()
    {
        if (RefreshManager.i.ARENAMODE)
        {
            StartCoroutine(ICheckPauseMenu());
            return;
        }
        if (!pauseUI.activeInHierarchy && canPauseMenu)
        {
            if ((!BM.i.UI.activeInHierarchy && !CharacterScript.CS.groupUIParent.activeInHierarchy && Building.CheckBuildingUIs()) || (TutorialManager.tutorial & !TutorialManager.building))
            {
                StartCoroutine(ICheckPauseMenu());
            }
        }
    }
   

    private IEnumerator ICheckPauseMenu()
    {
        yield return new WaitForSecondsRealtime(0.000001f);
        if(RefreshManager.i.ARENAMODE)
        {
            if (i.pauseUI.activeInHierarchy)
            {
                i.pauseUI.SetActive(false);
                SpawnManager.instance.CancelTS(pauseID);
            }
            else
            {
                IM.i.KeepControllerMoving();
                i.pauseUI.SetActive(true);
                pauseID = SpawnManager.instance.NewTS(0f, Mathf.Infinity);
            }
            yield break;
        }
        if((!BM.i.UI.activeInHierarchy && Building.CheckBuildingUIs()) || (TutorialManager.tutorial & !TutorialManager.building))
        {
            IM.i.KeepControllerMoving();
            i.pauseUI.SetActive(true);
            pauseID = SpawnManager.instance.NewTS(0f, Mathf.Infinity);
        }
    }


    private void ResetRaws()
    {
        foreach(RawImage img in rawImages)
        {
            img.texture = null;
            img.color = Color.black;
        }
    }

    public RawImage FillNextEEIcon()
    {
        foreach(RawImage img in rawImages)
        {
            if (img.texture == null)
            {
                img.transform.parent.gameObject.SetActive(true);
                return img;
            }
        }
        throw new Exception("Too many EE's for telephone!");
    }

    public void SetTelePhone(TeleMode mod, float t, Action onComplete = null) //setting raw image, camera pos, rotating, setting mod
    {
        if(mod != telemode && mod != TeleMode.Changing)
        {
            telemode = TeleMode.Changing;
            if(mod == TeleMode.Base)
            {
                PortalTrigger.i.col.enabled = true;
            }
            else
            {
                PortalTrigger.i.col.enabled = false;
            }
            StartCoroutine(SetTelePhoneI(mod,t,onComplete));
        }
    }

    private IEnumerator SetTelePhoneI(TeleMode mod, float t, Action onComplete)
    {
        for(float i = 0f; i < 90f; i+= 90 * Time.deltaTime * 2f / t)
        {
            teleTrans.localRotation = Quaternion.Euler(i, 0f,-60f);
            yield return null;
        }
        teleTrans.localRotation = Quaternion.Euler(90f, 0f, -60f);
        yield return null;
        telemode = mod;
        switch (mod)
        {
            case TeleMode.Core:
                telecam.orthographicSize = 2f;
                break;
            case TeleMode.Base:
                telecam.transform.position = new Vector3(-0.02f, 0.03f, -2f);
                telecam.orthographicSize = 0.935f;
                break;
            case TeleMode.Baron:
                telecam.orthographicSize = 1f;
                telecam.transform.position = new Vector3(-1000f, -1000f, -2f);
                break;
        }
        for (float i = 90f; i > 0; i -= 90f * Time.deltaTime * 2f / t)
        {
            teleTrans.localRotation = Quaternion.Euler(i, 0f, -60f);
            yield return null;
        }
        teleTrans.localRotation = Quaternion.Euler(0f,0f,-60f);
        onComplete?.Invoke();
    }

    public void AddBPImg(Sprite s)
    {
        foreach(Image img in lootImgs)
        {
            if(img.sprite == null)
            {
                img.sprite = s;
                LeanTween.value(img.gameObject, Color.clear, new Color(1f,1f,1f,0.5f), 1f).setOnUpdate((clr) => img.color = clr);
                LeanTween.moveLocalX(img.gameObject, img.transform.localPosition.x + 10f, 0.5f).setLoopPingPong(2);
                return;
            }
        }
    }

    public void DeleteBPImgs()
    {
        Invoke(nameof(DoDelete), 1f);
    }
    void DoDelete()
    {
        foreach (Image img in lootImgs)
        {
            if (img.sprite != null)
            {
                Instantiate(Resources.Load<GameObject>("cross"), img.transform.position, Quaternion.identity, img.transform);
                LeanTween.moveLocalX(img.gameObject, img.transform.localPosition.x + 10f, 0.2f).setLoopPingPong(10).setOnComplete(() => LeanTween.moveLocalX(img.gameObject, img.transform.localPosition.x - 100f, 1f).setOnComplete(() => { img.sprite = null; img.color = Color.clear; }));
            }
        }
    }

    public void SaveBPImgs()
    {
        Invoke(nameof(DoSave), 5.5f);
    }
    void DoSave()
    {
        foreach (Image img in lootImgs)
        {
            if (img.sprite != null)
            {
                Instantiate(Resources.Load<GameObject>("tick"), img.transform.position, Quaternion.identity, img.transform);
                LeanTween.moveLocalX(img.gameObject, img.transform.localPosition.x + 10f, 1f).setLoopPingPong(2).setOnComplete(() => LeanTween.value(img.gameObject, new Color(1f, 1f, 1f, 0.5f), Color.clear, 1f).setOnUpdate((clr) => img.color = clr).setOnComplete(() => { img.sprite = null; img.color = Color.clear; }));
            }
        }
    }

    public void FadeInCanvas()
    {
        if (noInCanvas) return;
        cg.LeanAlpha(1f, 1f).setIgnoreTimeScale(true);
    }

    public void FadeOutCanvas(bool quick = false)
    {
        cg.LeanAlpha(0f, quick ? 0.25f : 1f).setIgnoreTimeScale(true);
    }

    public void TurnOnEnergySliders(bool on)
    {
        foreach (GameObject g in energySliders)
        {
            g.SetActive(on);
        }

        if (!on)
        {
            otherSliders[0].anchoredPosition = new Vector2(0, -5);
            otherSliders[1].anchoredPosition = new Vector2(0, -105);
        }
        else
        {
            otherSliders[0].anchoredPosition = new Vector2(0, -105);
            otherSliders[1].anchoredPosition = new Vector2(0,-205);
        }
    }

    public static void MakeKey(string s, Vector2 pos, string descr = "", bool largeBox = false, bool careAboutHintsSettings = true)
    {
        if (!SetM.showKeys && careAboutHintsSettings) return;
        largeBox = !IM.controller && largeBox;
        if (keyGuides.ContainsKey(s))
        {
            Debug.LogWarning("Already has key: " + s);
            return;
        }
        var key = Instantiate(Resources.Load<GameObject>("Key"), pos, Quaternion.identity, GS.FindParent(GS.Parent.misc));
        if (largeBox)
        {
            key.GetComponent<SpriteRenderer>().sprite = Resources.Load<Sprite>("SpaceBar");
        }
        if (i.keyOverrides.Contains(s) && !IM.controller)
        {
            Image img = key.GetComponentInChildren<Image>();
            img.sprite = i.keyOverrideImages[Array.IndexOf(i.keyOverrides,s)];
            img.color = new Color(1f,1f,1f,0.7f);
        }
        else
        {
            if (IM.controller)
            {
                var img = key.GetComponentInChildren<Image>();
                img.sprite = i.KeySpriteFromLetter(s);
                img.color = new Color(1f,1f,1f,0.7f);
            }
            else
            {
                key.GetComponentsInChildren<TextMeshPro>()[0].text = s;
            }
        }
        if (descr != null)
        {
            key.GetComponentsInChildren<TextMeshPro>()[1].text = descr;
        }
        key.transform.localScale = Vector3.zero;
        key.SetActive(true);
        key.LeanScale(Vector3.one, 0.8f).setEaseOutSine().setIgnoreTimeScale(true);
        keyGuides.Add(s,key);
    }
    
    public static void MakeUIKey(string s, Transform t, string descr = "", bool largeBox = false, bool careAboutHintsSettings = true)
    {
        if (!SetM.showKeys && careAboutHintsSettings) return;
        largeBox = !IM.controller && largeBox;
        if (keyGuides.ContainsKey(s))
        {
            Debug.LogWarning("Already has key: " + s);
            return;
        }
        var key = Instantiate(Resources.Load<GameObject>("Key"), Vector2.zero, Quaternion.identity,t);
        if (largeBox)
        {
            key.GetComponent<SpriteRenderer>().sprite = Resources.Load<Sprite>("SpaceBar");
        }
        if (i.keyOverrides.Contains(s) && !IM.controller)
        {
            Image img = key.GetComponentInChildren<Image>();
            img.sprite = i.keyOverrideImages[Array.IndexOf(i.keyOverrides,s)];
            img.color = new Color(1f,1f,1f,0.7f);
        }
        else
        {
            if (IM.controller)
            {
                var img = key.GetComponentInChildren<Image>();
                img.sprite = i.KeySpriteFromLetter(s);
                img.color = new Color(1f,1f,1f,0.7f);
            }
            else
            {
                key.GetComponentsInChildren<TextMeshPro>()[0].text = s;
            }
        }
        if (descr != null)
        {
            key.GetComponentsInChildren<TextMeshPro>()[1].text = descr;
        }
        key.transform.localScale = Vector3.zero;
        key.SetActive(true);
        key.LeanScale(Vector3.one, 0.8f).setEaseOutSine().setIgnoreTimeScale(true);
        keyGuides.Add(s,key);
    }


    public Sprite KeySpriteFromLetter(string s)
    {
        return keysprites[Array.IndexOf(letters,s.ToUpper())];
    }

    public static void DeleteKey(string s)
    {
        if (keyGuides.Remove(s, out var guide))
        { 
            guide.LeanCancel();
            guide.LeanScale(Vector3.zero, 1f).setEaseInSine().setOnComplete(() => Destroy(guide));
        }
    }
    
    
}
