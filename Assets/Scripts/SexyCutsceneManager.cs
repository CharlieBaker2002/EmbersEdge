using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

public class SexyCutsceneManager : MonoBehaviour
{
    int sceneToLoad = 1;

    [Tooltip("How long it takes for the black screen to sweep in.")]
    public float blackScreenSweepDuration = 1.0f;

    [Header("UI References")]
    [Tooltip("RectTransform for the black screen. Should cover entire screen when anchored at X=0.")]
    public RectTransform blackScreenRect;

    [Tooltip("RectTransform for the hint display (text or image).")]
    public RectTransform hintRect;

    [Header("Hint Movement Settings")]
    [Tooltip("Time it takes for a hint to move onto the screen.")]
    public float hintMoveInTime = 1.0f;

    [Tooltip("Time the hint sits still in the center before moving out.")]
    public float hintPauseTime = 1.5f;

    [Tooltip("Time it takes for the hint to move off the screen.")]
    public float hintMoveOutTime = 1.0f;

    [Tooltip("List of hint messages or images you want to show.")]
    public List<string> hintMessages;

    public List<string> extraMessagesForArena;
    public List<string> extraMessagesForPlay;

    public TextMeshProUGUI hintText;

    private AsyncOperation asyncLoad;

    public static SexyCutsceneManager i;

    [SerializeField] private ParticleSystem ps;
    
    private bool loading = false;

    [SerializeField] private GameObject baseUI;
    [SerializeField] private GameObject coreicon;
    
    private void Awake()
    {
        i = this;
    }

    public void Cutscene(int buildIndex)
    {
        if (loading) return;
        ps.Stop();
        sceneToLoad = buildIndex;
        StartCoroutine(CutsceneSequence());
    }

    private IEnumerator CutsceneSequence()
    {
        yield return StartCoroutine(SweepBlackScreenIn());
        yield return StartCoroutine(LoadYourAsyncScene());
    }

    private IEnumerator SweepBlackScreenIn()
    {
        blackScreenRect.anchoredPosition = new Vector2(-2f * Screen.width, 0);

        LeanTween.moveX(blackScreenRect, 0f, blackScreenSweepDuration)
                 .setEase(LeanTweenType.easeInOutQuad);
        

        yield return new WaitForSeconds(blackScreenSweepDuration);
        baseUI.gameObject.SetActive(false);
        if (sceneToLoad == 1)
        {
            coreicon.gameObject.SetActive(true);
        }
     
    }

    private IEnumerator LoadYourAsyncScene()
    {
        asyncLoad = SceneManager.LoadSceneAsync(sceneToLoad);
        if (asyncLoad == null)
        {
            SceneManager.LoadScene(sceneToLoad);
        }
        else
        {
            asyncLoad.allowSceneActivation = false;
            while (!asyncLoad.isDone)
            {
                for (int i = 0; i < 2; i++)
                {
                    ShowRandomHint(ref hintMessages);
                    yield return new WaitForSeconds(hintMoveInTime + hintPauseTime + hintMoveOutTime + 0.5f);
                }
                if (asyncLoad.progress >= 0.9f)
                {
                    if (sceneToLoad == 1)
                    {
                        hintPauseTime = 4f;
                        int length = extraMessagesForPlay.Count;
                        for (int z = 0; z < length; z++)
                        {
                            ShowRandomHint(ref extraMessagesForPlay);
                            yield return new WaitForSeconds(hintMoveInTime + hintPauseTime + hintMoveOutTime + 0.5f);
                        }
                    }
                    else
                    {
                        int length= extraMessagesForArena.Count;
                        for (int z = 0; z < length; z++)
                        {
                            ShowRandomHint(ref extraMessagesForArena);
                            yield return new WaitForSeconds(hintMoveInTime + hintPauseTime + hintMoveOutTime + 0.5f);
                        }
                    }
                    asyncLoad.allowSceneActivation = true;
                    yield break;
                }
                yield return null;
            }
        }
       
    }

    private void ShowRandomHint(ref List<string> strs)
    {
        int randomIndex = Random.Range(0, strs.Count);
        string chosenHint = strs[randomIndex];
        strs.RemoveAt(randomIndex);
        
        hintRect.localScale = Vector2.zero;
        hintText.text = chosenHint;
       
        hintRect.anchoredPosition = new Vector2(-Screen.width, hintRect.anchoredPosition.y);
        
        LeanTween.scale(hintRect, Vector2.one, hintMoveInTime).setEase(LeanTweenType.easeOutSine);
        LeanTween.moveX(hintRect, 0f, hintMoveInTime)
                 .setEase(LeanTweenType.easeOutCirc)
                 .setOnComplete(() =>
                 {
                     LeanTween.delayedCall(hintPauseTime, () =>
                     {
                         LeanTween.scale(hintRect, Vector2.zero, hintMoveOutTime).setEase(LeanTweenType.easeInOutCirc);
                         LeanTween.moveX(hintRect, Screen.width, hintMoveOutTime)
                                  .setEase(LeanTweenType.easeInOutCirc);
                     });
                 });
    }
}