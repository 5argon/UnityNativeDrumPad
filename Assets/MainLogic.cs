using System.Collections;
using System.Collections.Generic;
using System.Linq;
using E7.Native;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

public class MainLogic : MonoBehaviour
{
    public Toggle nativeAudio;
    public Toggle nativeTouch;

    [Space]

    public Image nativeAudioImage;
    public Image nativeTouchImage;
    public Color nativeAudioActiveGridColor;
    public Color nativeTouchActiveGridColor;

    private Color nativeAudioOriginalGridColor;
    private Color nativeTouchOriginalGridColor;
    private static Vector2Int cachedStaticRealScreenResolution;

    public bool NativeAudioChecked => nativeAudio.isOn;
    public bool NativeTouchChecked => nativeTouch.isOn;

#if NATIVE_AUDIO
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        //This is so that it comes before all Pad's `Awake` which attempts to load the audio.
        NativeAudio.Initialize();
    }
#endif

    /// <summary>
    /// This cannot be Awake, at Awake the uGUI layout system is still not ready and <see cref="Canvas.ForceUpdateCanvases"> returns something wrong.
    /// </summary>
    public void Start()
    {
        //This is to make all the RectTransform layout calculates first.
        //Alternatively you could use `IEnumerator` Start then just `yield return null` to wait a frame.
        Canvas.ForceUpdateCanvases(); 
        PrecalculateRect();

        //Make the toggle disappear for those without the plugins.
        ShowHideToggleOnPreprocessor();

        this.nativeAudioOriginalGridColor = nativeAudioImage.color;
        this.nativeTouchOriginalGridColor= nativeTouchImage.color;
        NativeAudioToggleUpdate();
        NativeTouchToggleUpdate();
    }

    private void ShowHideToggleOnPreprocessor()
    {
#if !NATIVE_AUDIO
        nativeAudio.gameObject.SetActive(true);
#endif
#if !NATIVE_TOUCH
        nativeTouch.gameObject.SetActive(true);
#endif
    }

    public void NativeAudioToggleUpdate()
    {
#if NATIVE_AUDIO
        if (NativeAudioChecked)
        {
            nativeAudioImage.color = nativeAudioActiveGridColor;
        }
        else
        {
            nativeAudioImage.color = nativeAudioOriginalGridColor;
        }
#endif
    }

    private static List<(Rect rect, Pad pad)> rectAndPad = new List<(Rect, Pad)>(16);

    public Pad[] allPads;
    public RectTransform rootCanvasRt;

    /// <summary>
    /// On awake or device rotation, precalculate the rect to be checked with <see cref="NativeTouchData"> on native callback from the OS in static scope.
    /// That static scope strage is <see cref="rectAndPad">.
    /// 
    /// It is then linked with each pad, which is holding <see cref="NativeAudioPointer"> for us to call <see cref="NativeAudioPointer.Play(NativeAudio.PlayOptions)">.
    /// By able to play Native Audio directly in the touch callback, we have linked both strength of Native Audio and Native Touch. This "static preparation" is to
    /// enable that. 
    /// 
    /// Or else, we would have to wait for some main thread code which could access all the <see cref="Pad"> normally, and check the touches from
    /// <see cref="NativeTouch.touches"> instead.
    /// </summary>
    public void PrecalculateRect()
    {
#if NATIVE_TOUCH
        rectAndPad.Clear();
        foreach(Pad p in allPads)
        {
            var rt = p.GetComponent<RectTransform>();

            var v3 = new Vector3[4];

            // Each corner provides its world space value. The returned array of 4 vertices is clockwise.
            // It starts bottom left and rotates to top left, then top right, and finally bottom right. 
            // Note that bottom left, for example, is an (x, y, z) vector with x being left and y being bottom.
            rt.GetWorldCorners(v3);

            for (int i = 0; i < 4; i++)
            {
                v3[i] = rootCanvasRt.InverseTransformPoint(v3[i]);
            }

            //var rectRelativeToCanvasRoot = new Rect(v3[0].x, v3[0].y, v3[3].x - v3[0].x, v3[1].y - v3[0].y);

            //^^^ This is almost correct, however it is referenced from the center of the canvas since the root canvas pivot is locked at 0.5,0.5
            //We fix this by adding to the rect position point by half of the size.

            var canvasSize = rootCanvasRt.sizeDelta;
            var halfCanvasSize = new Vector2(canvasSize.x / 2, canvasSize.y / 2);

            var rectXPos = v3[0].x + halfCanvasSize.x;
            var rectYPos = v3[0].y + halfCanvasSize.y;
            var rectWidth = v3[3].x - v3[0].x;
            var rectHeight = v3[1].y - v3[0].y;

            //Next we will make this canvas coordinated rect a screen space instead so at callback we have minimal work to do.

            //Here we cannot use `Screen.___` API since if "(Dynamic) Resulution Scaling" is used that will be changing.
            //But luckily touches from Native Touch stays true to the device's native screen size. 
            //What size we have to convert to have to be relative to native screen size. (Of course if we rotate screen, we have to redo the whole thing again.)

            cachedStaticRealScreenResolution = NativeTouch.RealScreenResolution();
#if UNITY_EDITOR
            cachedStaticRealScreenResolution = Vector2Int.one;
#endif
            var rectRelativeToRealScreen = new Rect(
                x: (rectXPos / canvasSize.x) * cachedStaticRealScreenResolution.x,
                y: (rectYPos / canvasSize.y) * cachedStaticRealScreenResolution.y,
                width: rectWidth,
                height: rectHeight
            );

            Debug.Log($"Precalculating : {p.name} {rectRelativeToRealScreen} {rt.anchoredPosition} {rt.sizeDelta}");
            rectAndPad.Add((rectRelativeToRealScreen, p));
        }
#endif
    }

#if NATIVE_TOUCH
    /// <summary>
    /// Now instead of relying on `EventTrigger` + `GraphicRaycaster` finding the correct <see cref="Pad"> for us, we must use this
    /// <paramref name="ntd"> directly to find out which pad was touched.
    /// 
    /// This callback is called by the OS, IF the callback is fast, this will be faster than `EventTrigger`. And by using Native Audio here
    /// we will in effect get the fastest audio playing in response to a touch.
    /// 
    /// Since native only know you app's `static` scope, we need to "migrate" what's needed to be compute with <paramref name="ntd"> to `static`.
    /// That is we need to know all <see cref="Pad">'s screen space rectangle to check which pad's audio we should play.
    /// 
    /// This callback is not necessary in the same thread as Unity (it is like that on Android), so you need to be careful what you could do here.
    /// Luckily Native Audio works (<see cref="AudioSource.Play"> crash Unity on other thread).
    /// 
    /// Also I avoid Unity physics/raycast stuff since it doesn't work cross thread. Even if it works I think a manual solution could be better.
    /// 
    /// If you are using Android IL2CPP, the callback is slow so it should not be used. Instead you should check on <see cref="NativeTouch.touches"> somewhere
    /// in your main thread's normal code. It is still faster than <see cref="Input.touches">.
    private static void NativeCallback(NativeTouchData ntd)
    {
        //Only care about the down moment, just like "pointer down" on the `EventTrigger`.
        if(ntd.Phase != TouchPhase.Began) return;
        Debug.Log($"{ntd}");

        //Don't forget to flip the Y axis coming from native side. The rects to be compared with are computed from `RectTransform` and they are under Unity's convention.
        //You could also flip those rects in the precalculation to save this flip's CPU cycle..
        //But I am afraid it might make that logic difficult to understand than flipping the input here.
        var touchCoordinate = new float2(ntd.X, cachedStaticRealScreenResolution.y - ntd.Y);

        //This is a linear loop, which makes the 16th pad sliiiiiightly slower because it need the run through all other pads.
        //You can further do micro-optimization (may not be the root of all evil?) by for example, do a binary search based on incoming touch coordinate.
        //e.g. If it is in the top half of the screen, then no reason to search the lower half pads.
        //But I am not going to do that for simplicity.
        foreach (var item in rectAndPad)
        {
            if (RectContains(item.rect, touchCoordinate))
            {
                Debug.Log($"Rect {item.rect} hitted!");

                //This is assuming no one is modifying anything on each Pad. And we know FOR THIS APP that that is the case.
                //Or else you have to properly put `lock(___)` on the reference type item you fear the main thread might be using at the same time.

                //This method will play an audio natively. If native audio is not enabled, it will play with normal means.
                //But since this is the callback context and on Android thread is not compatible, `comingFromCallback` will
                //help us check and prevent the play.
                item.pad.PlayAudio(comingFromCallback: true);

                //You will hear an audio right now, even before the button lights up due to `EventTrigger`!
                break;
            }
        }

        bool RectContains(in Rect rect, float2 point)
        {
            return (point.x >= rect.xMin) && (point.x < rect.xMax) && (point.y >= rect.yMin) && (point.y < rect.yMax);
        }
    }
#endif


    /// <summary>
    /// Called on toggling the native touch toggle.
    /// 
    /// To play the audio as fast as possible, Native Audio code should be directly in Native Touch's callback that OS called.
    /// 
    /// Because Native Audio can play audio out of frame and no need to wait for the end of frame like Unity's <see cref="AudioSource.Play">, this is the fastest
    /// if the audio is a result of an input.
    /// 
    /// However, this is only if the callback is fast. I found that Android Mono and iOS IL2CPP has great callback performance, but not on 
    /// Android IL2CPP. So we will use a "ring buffer iteration" instead of callback if we are using IL2CPP on Android. See the website for explanation.
    /// 
    /// IL2CPP compile or not is checked by the manually defined preprocessor ANDROID_MONO. It is not there by default, so put that in manually if you want to go Mono.
    /// </summary>
    public void NativeTouchToggleUpdate()
    {
#if NATIVE_TOUCH
        if (NativeTouchChecked)
        {
            nativeTouchImage.color = nativeTouchActiveGridColor;
            NativeTouch.ClearCallbacks();

            bool useCallback = true;
#if !ANDROID_MONO
            useCallback = false;
#endif
            if (useCallback)
            {
                NativeTouch.RegisterCallback(NativeCallback);
            }
            NativeTouch.Start(new NativeTouch.StartOption { noCallback = useCallback ? false : true });
        }
        else
        {
            nativeTouchImage.color = nativeTouchOriginalGridColor;
            NativeTouch.Stop();
            NativeTouch.ClearCallbacks();
        }
#endif
    }

}
