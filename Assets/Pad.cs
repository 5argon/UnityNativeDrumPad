using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using E7.Native;
using UnityEngine;
using UnityEngine.UI;

public class Pad : MonoBehaviour
{
    public Color downColor;
    public Image image;
    public AudioSource selfSource;

    private Color normalColor;
    private MainLogic mainLogic;

    private NativeAudioPointer loadedNativeAudio;

    /// <summary>
    /// This property is possible to be accessed from the OTHER THREAD.
    /// That is, on Android Mono where OS call the Native Touch callback from non Unity main thread. iOS IL2CPP also, but that's in the same thread so no problem.
    /// 
    /// Ensure that no race condition happen on access is important. We ensure that since main thread only assign to this variable on the line above
    /// and no main thread ever change this variable ever again. If this is not the case, you should use `lock` keyword when accessing from the other thread
    /// so main thread could not mess with it while other thread is using.
    /// </summary>
    public NativeAudioPointer LoadedNativeAudio => loadedNativeAudio;

    public void Awake()
    {
        normalColor = image.color;
        mainLogic = GameObject.FindObjectOfType<MainLogic>();
        if (NativeAudio.OnSupportedPlatform())
        {
            loadedNativeAudio = NativeAudio.Load(selfSource.clip);
        }
    }

    /// <summary>
    /// Play audio natively or normally based on if we have Native Audio support or not + did we check the checkbox or not.
    /// </summary>
    public void PlayAudio(bool comingFromCallback)
    {
#if NATIVE_AUDIO
        if (NativeAudio.OnSupportedPlatform() && mainLogic.NativeAudioChecked)
        {
            loadedNativeAudio.Play();
        }
        else
        {
            //If from callback on Android, we could not let it use main thread only things like `Stop` and `Play` of `AudioSource`. Or you will get :
            /*
                Unity   E  UnityException: Stop can only be called from the main thread.
                        E  Constructors and field initializers will be executed from the loading thread when loading a scene.
                        E  Don't use this function in the constructor or field initializers, instead move initialization code to the Awake or Start function.
             */
#if UNITY_ANDROID
            if (!comingFromCallback)
#endif
            {
                selfSource.Stop();
                selfSource.Play();
            }
        }
#else
            selfSource.Stop();
            selfSource.Play();
#endif
    }

    /// <summary>
    /// Do not play audio on EventTrigger down if we have Native Touch and it is enabled.
    /// We can do it faster via callbacks and ring buffer iteration instead.
    /// 
    /// Regardless of modes, the light up logic will be run by `EventTrigger`.
    /// </summary>
    public void Down()
    {
        //The light up is working by `EventTrigger` no matter using Native Touch or not.
        image.color = downColor;

        if (!(NativeTouch.OnSupportedPlatform() && mainLogic.NativeTouchChecked))
        {
            PlayAudio(comingFromCallback: false);
        }
    }


    /// <summary>
    /// Regardless of modes, the light up logic will be run by `EventTrigger`.
    /// </summary>
    public void Up()
    {
        image.color = normalColor;
    }
}
