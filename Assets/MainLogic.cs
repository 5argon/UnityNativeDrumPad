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
    [Header("Check this manually if you are using IL2CPP on Android. It will avoid using touch callback to play audio.")]
    public bool buildingWithIl2cppOnAndroid;

    [Space]

    public Toggle nativeAudio;
    public Toggle nativeTouch;

    [Space]

    public Image nativeAudioImage;
    public Image nativeTouchImage;
    public Color nativeAudioActiveGridColor;
    public Color nativeTouchActiveGridColor;

    private Color nativeAudioOriginalGridColor;
    private Color nativeTouchOriginalGridColor;

    public bool NativeAudioChecked => nativeAudio.isOn;
    public bool NativeTouchChecked => nativeTouch.isOn;

    /// <summary>
    /// This cannot be Awake, at Awake the uGUI layout system is still not ready and <see cref="Canvas.ForceUpdateCanvases"> returns something wrong.
    /// </summary>
    public void Start()
    {
        //This is to make all the RectTransform layout calculates first.
        //Alternatively you could use `IEnumerator` Start then just `yield return null` to wait a frame.
        Canvas.ForceUpdateCanvases(); 
        PrecalculateRect();

        this.nativeAudioOriginalGridColor = nativeAudioImage.color;
        this.nativeTouchOriginalGridColor= nativeTouchImage.color;
        NativeAudioToggleUpdate();
        NativeTouchToggleUpdate();

        NativeAudio.Initialize();
    }

    public void NativeAudioToggleUpdate()
    {
        if(NativeAudioChecked)
        {
            nativeAudioImage.color = nativeAudioActiveGridColor;
        }
        else
        {
            nativeAudioImage.color = nativeAudioOriginalGridColor;
        }
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
    private void PrecalculateRect()
    {
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

            //^^^ This is almost correct, however it is referenced from the center of the canvas since the pivot is locked at 0.5,0.5
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
            //What size we have to convert to is relative to native screen size. (Of course if we rotate screen, we have to redo the whole thing again.)

            Vector2Int realScreenResolution = NativeTouch.RealScreenResolution();
#if UNITY_EDITOR
            realScreenResolution = Vector2Int.one;
#endif
            var rectRelativeToRealScreen = new Rect(
                x: (rectXPos / canvasSize.x) * realScreenResolution.x,
                y: (rectYPos / canvasSize.y) * realScreenResolution.y,
                width: rectWidth,
                height: rectHeight
            );

            Debug.Log($"{p.name} {rectRelativeToRealScreen} {rt.anchoredPosition} {rt.sizeDelta}");
            rectAndPad.Add((rectRelativeToRealScreen, p));
        }
    }


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
    /// If you are using Android IL2CPP, the callback is slow so it should not be used. Instead you should check on <see cref="NativeTouch.touches"> somewhere
    /// in your main thread's normal code. It is still faster than <see cref="Input.touches">.
    private static void NativeCallback(NativeTouchData ntd)
    {
        new float2(ntd.
        foreach(var item in rectAndPad)
        {
            if(RectContains( item.rect,
        }

        bool RectContains(in Rect rect, float2 point)
        {
            return (point.x >= rect.xMin) && (point.x < rect.xMax) && (point.y >= rect.yMin) && (point.y < rect.yMax);
        }
    }


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
    /// IL2CPP compile is checked by just the checkbox <see cref="buildingWithIl2cppOnAndroid"> on this component.
    /// </summary>
    public void NativeTouchToggleUpdate()
    {
        if (NativeTouchChecked)
        {
            nativeTouchImage.color = nativeTouchActiveGridColor;
            NativeTouch.ClearCallbacks();


            bool useCallback = true;
#if UNITY_ANDROID
            useCallback = buildingWithIl2cppOnAndroid ? false : true;
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
    }
}
