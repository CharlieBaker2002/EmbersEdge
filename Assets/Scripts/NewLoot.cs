using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class NewLoot : MonoBehaviour, IHoverable
{
    [SerializeField] private Image img;
    [SerializeField] private Image taipImg;
    [SerializeField] private TextMeshProUGUI nText;
    [SerializeField] private TextMeshProUGUI dayText;
    [SerializeField] private TextMeshProUGUI nam;
    [SerializeField] private TextMeshProUGUI description;
    [SerializeField] private Sprite[] EEIconSprites;
    [SerializeField] private RectTransform border;
    [SerializeField] private RectTransform background;

    [SerializeField] private TextMeshProUGUI evolutionsText;
    [SerializeField] private GameObject evolutionsButton;
    
    [SerializeField] public LootSelectButton select;
    public LootEvolutionButton[] evs;
    [SerializeField] private EvolutionsButton evButton;

    private Coroutine scroll;
    
    public Blueprint bp;
    public bool chosen;
    
    private bool hovering;
    private float hoverTime;
    private bool en = true;

    private IEnumerator Start()
    {
        en = true;
        yield return null;
        yield return null;
        if (bp is MechanismSO s)
        {
            PopulateWithMechanism(s, true);
        }
        else
        {
            PopulateOtherwise();
        }
        LeanTween.scale(gameObject,Vector3.one * 0.8f, 0.4f).setEaseOutBack().setIgnoreTimeScale(true);
    }

    public void PopulateWithMechanism(MechanismSO s, bool changeEvs)
    {
        taipImg.sprite = UIManager.i.partClassifierImages[(int)s.p.taip];
        img.sprite = s.s;
        img.preserveAspect = true;
        nam.text = s.name;
        if (description.text != s.description)
        {
            if (scroll != null)
            {
                StopCoroutine(scroll);
            }
            description.text = "";
            scroll = StartCoroutine(ScrollText(description, s.description));
        }
        dayText.text = s.powerRequired.ToString();
        nText.text = "1 / " + (s.relevents.Count + 1);
       
        if (s.relevents.Count == 0 && changeEvs)
        {
            evolutionsText.text = "No Evolutions";
            evolutionsButton.tag = "Misc";
        }
        
        else if (changeEvs)
        {
            evButton.LoadEvolutions(true);
        }
    }

    public void PopulateOtherwise()
    {
        img.sprite = bp.s;
        img.preserveAspect = true;
        nam.text = bp.name;
        taipImg.sprite = UIManager.i.partClassifierImages[0]; //bonus bb
        evolutionsButton.gameObject.SetActive(false);
    }
    
    public void OnDeHover()
    {
        if(!enabled) return;
        LeanTween.cancel(gameObject);
        LeanTween.scale(gameObject, Vector3.one * 0.8f, 0.75f).setEaseOutElastic().setIgnoreTimeScale(true);
        en = true;
    }

    public void OnHover()
    {
        LeanTween.cancel(gameObject);
        LeanTween.scale(gameObject, Vector3.one, 0.8f).setEaseOutBack();
        if (en)
        {
            en = false;
            LeanTween.cancel(background.gameObject);
            LeanTween.rotate(background.gameObject, Vector3.zero, 0.45f).setEaseOutQuart().setIgnoreTimeScale(true);
            LeanTween.moveLocal(background.gameObject, Vector3.zero, 0.45f).setEaseOutQuart().setIgnoreTimeScale(true);
        }
    }
    
    /// if mouse is in the centre of the object, then the rotation should be none. Otherwise, rotate the card by the x and y axis by up to 15 degrees, based on the mouse's position within the background image.
    private void Update()
    {
        if (!en) return;
        Vector3 mousePos = IM.i.MouseScreen();

        Vector2 normalizedPoint = new Vector3((mousePos.x - transform.position.x)/Screen.width, (mousePos.y -transform.position.y)/Screen.height);

        float rotationX = normalizedPoint.x * 35f;
        float rotationY = normalizedPoint.y * normalizedPoint.y * 100f;
        background.anchoredPosition = Vector2.Lerp(background.anchoredPosition,
            new Vector2(normalizedPoint.x * 60f, normalizedPoint.y * 5f), Time.unscaledDeltaTime * 3f);
        background.localRotation = Quaternion.Lerp(background.localRotation, Quaternion.Euler(-rotationX, Mathf.Sign(rotationY) * 20f, -rotationX * rotationY * 0.01f), Time.deltaTime * 3f);
    
        transform.localScale = Vector3.Lerp(transform.localScale,Vector3.one * (0.8f * (0.5f + 0.5f*Mathf.Exp(-3f*Mathf.Abs(normalizedPoint.x)))), 3f * Time.unscaledDeltaTime);
    }

    public void End()
    {
        LeanTween.cancel(gameObject);
        en = false;
        foreach (Transform t in transform.GetComponentsInChildren<Transform>(false))
        {
            t.tag = "Misc";
            LeanTween.cancel(t.gameObject);
        }

        enabled = false;
        select.en = false;
        RefreshManager.i.QA(() => LeanTween.scale(gameObject, Vector3.zero, 1f).setEaseInBounce().setIgnoreTimeScale(true)
            .setOnComplete(() => Destroy(gameObject)),0.1f);
    }

    #region Helper
    IEnumerator FadeInImage(Image img)
    {
        while (img.color.a < 0.98f)
        {
            img.color = new Color(1,1,1,Mathf.Lerp(img.color.a, 1f,0.02f));
            yield return new WaitForSecondsRealtime(0.02f);
        }
        img.color = new Color(1, 1, 1, 1);
    }

    IEnumerator FadeInText(TextMeshProUGUI text)
    {
        while (text.color.a < 0.98f)
        {
            text.color = new Color(text.color.r, text.color.g, text.color.b, Mathf.Lerp(text.color.a, 1, 0.02f));
            yield return new WaitForSecondsRealtime(0.02f);
        }
        text.color = new Color(text.color.r, text.color.g, text.color.b, 1);
    }

    IEnumerator FadeOutImage(Image img)
    {
        float t = 0f;
        while (t < 0.5f)
        {
            img.color = new Color(1, 1, 1, Mathf.Lerp(img.color.a, 0, 0.1f));
            t += 0.02f;
            yield return new WaitForSecondsRealtime(0.02f);
        }
        img.color = new Color(1, 1, 1, 0);
    }

    IEnumerator FadeOutText(TextMeshProUGUI text)
    {
        float t = 0f;
        while (t < 0.5f)
        {
            text.color = new Color(text.color.r, text.color.g, text.color.b, Mathf.Lerp(text.color.a, 0, 0.1f));
            t += 0.02f;
            yield return new WaitForSecondsRealtime(0.02f);
        }
        text.color = new Color(1, 1, 1, 0);
    }

    public static IEnumerator ScrollText(TextMeshProUGUI text, string s)
    {
        float speed = 1f/60f;
        for(int i = 0; i < s.Length; i++)
        {
            text.text += s[i];
            yield return new WaitForSecondsRealtime(speed);
        }
    }
    #endregion
}
