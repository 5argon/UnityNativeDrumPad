# Unity Native Drum Pad

This project show the lowest latency way I could think of in making an iOS/Android musical app in Unity.

Musical app do not just need fast audio playing, but also fast input processing because most audio is a result of user's interaction. In this project I show how to use [Native Audio](http://exceed7.com/native-audio/) and [Native Touch](http://exceed7.com/native-touch) link up together to reduce both actual latency and perceived latency.

## Dependencies

Native Audio and Native Touch is a UPM dependency linked to local UPM in my own machine, as you can see in `manifest.json`.

```
    "com.e7.native-audio": "file:/Users/Sargon/Documents/Projects/NativeAudio/Assets/NativeAudio",
    "com.e7.native-touch": "file:/Users/Sargon/Documents/Projects/NativeTouch/Assets/NativeTouch",
```

If you want to try this out, you should comment out these 2 lines and import the package from Asset Store, or you can also link to Native Audio and Native Touch on your hard disk as local UPM like mine.

## Important project settings

- The Project Settings > Audio must be on "Best Latency". It doesn't affect Native Audio on Android but it does affect Native Audio on iOS, since the buffer size is shared in common for Unity too.

## Idea

- Each pad has its own `AudioSource`, `EventTrigger` on pointer down will play that source. The source is already connected with an `AudioClip`, so just `audioSource.Play()` will play the correct audio.
- Initialize both Native Audio and Native Touch. Native Audio will allocate native sources, Native Touch will register a `static` callback that the OS could call into C# from Java/Objective C immediately on touch.
- On `Awake` each pad also yank its `AudioClip` from the `AudioSource` to do `NativeAudio.Load(audioClip)`. The audio bytes are now copied to native side for fast playing.
- On `Start` each `RectTransform` of the `Pad` will be calculated to screen space `Rect`. `Rect` is a `struct` and allows us to use them safely from other thread (where the touch callback from OS might be) and also further boost checking performance with C# Jobs / ECS. This screen space is not from `Screen.width/height` API, but from `NativeTouch.RealScreenResoluion()`. Touches from the native side do not know if Unity is using (Dynamic) Resolution Scaling and is always relative to unscaled screen. This is to get ready ahead of time to be computed with touch from native (`NativeTouchData`) that Native Touch will give us.
- On screen rotation, the `RectTransform` will be recalculated. This app supports

The behaviour of the app now depends on 2 `Toggle` on the side of the screen, used to turn on and off Native Audio and Native Touch and compare performance. I will explain them in details for educational purpose.

### Native Audio : 0FF / Native Touch : OFF

1. On touch, at respective native side callback on native "view" the touch went into the closed source engine code.
2. After some processing, the touch appears in `Input.touches` polling API in C#. Depending on when you touch the screen and platform, this touch data may or may not appear in the immediate next frame.
3. The only `EventSystem` in the scene will use that touch data with `GraphicRaycaster` to raycast into the `Canvas` tree.
4. The ray hit one of the pad and called `EventTrigger` on it. The event is "on pointer down" and it calls `Down` on the `Pad`.
5. `Down` will play `AudioSource` on it, it was connected with the correct `AudioClip`, so you hear a sound.
6. The `audioSource.Play()` command is not played immediately, but at the end of this frame. Because it had to wait to mix with potentially other sounds and for the effect chain of Unity mixer.

### Native Audio : **ON** / Native Touch : OFF

1. Follow 1. ~ 5. of the OFF OFF case.
2. The `audioSource.Play()` command is not played immediately, but at the end of this frame. Because it had to wait to mix with potentially other sounds and for the effect chain of Unity mixer.
3. `Down` found that Native Audio is enabled, and instead use the stored `NativeAudioPointer` in that `Pad`, which was returned from the native load with `AudioClip` at start.
4. The `nativeAudioPointer.Play()` method instruct the native side to find the loaded audio byte array and play it immediately at that point in code. No need to wait for the end of frame.

### Native Audio : OFF / Native Touch : **ON**

1. On touch, at respective native side callback on native "view", but before the touch went into the closed source engine code, we write that touch into a "ring buffer" which C# could also read from.
2. Still in native side, if it is iOS (IL2CPP) where the callback speed is good, we are calling remembered C# delegate at this moment the touch happening. If it is either Android Mono (the callback speed is good, but we could do nothing in the callback as I will explain next) or Android IL2CPP (the callbck speed sucks, so we are avoiding that) we set up Native Touch so that `noCallback` is enabled.
3. On iOS IL2CPP, At C#, the `static` callback code try to use the incoming `NativeTouchData` to find out which `Pad` was hit without help from `GraphicRaycaster`.
4. After found out which `Pad` was hit, on iOS since this callback from Objective-C is in the same thread as Unity, we can execute `audioSource.Play()` on that pad right in the callback. Since it is `AudioSource`, the command is queued and collected to be processed at the end of frame and mix together. The job on iOS is now finished.
5. On Android Mono/IL2CPP the callback is in the other thread, `AudioSource` is not usable cross-thread, but we have disabled the callback already so we are skipping 2. 3. and 4.
6. The touch at native side is finally sent to the closed source touch processing engine code. But we are not relying on that appearing on `Input.touches` or the `EventTrigger` at all.
7. Back in Unity, when it is `MainLogic`'s turn for `Update` we check the "ring buffer" with `NativeTouch.touches`. The touch here is potentially appearing faster than `Input.touches` especially on Android since touch processing is in concurrent with Unity. So if we hit the screen right before the end of a frame, we "won" because the touch is written to the ring buffer, but the touch that goes to Unity closed source processing hadn't finished processing yet and not ready for the next frame that came too soon and not waiting for the touch since the processing is not in the main thread.
8. After checking we use the same logic as 3. and 4. to find the hit and play audio. The job for both Android Mono and IL2CPP is finished. It does not matter if `MainLogic`'s `Update` came sooner or later in a frame since `AudioSource` cannot play mid-frame. So putting it in just `MainLogic` is enough.

### Native Audio : **ON** / Native Touch : **ON**

1. On touch, at respective native side callback on native "view", but before the touch went into the closed source engine code, we write that touch into a "ring buffer" which C# could also read from.
2. Still in native side, if it is iOS (IL2CPP) **and** Android Mono where the callback speed is good, we are calling remembered C# delegate at this moment the touch happening. If it is Android IL2CPP where the callbck speed sucks, we set up Native Touch so that `noCallback` is enabled.
3. On iOS IL2CPP **and** Android Mono, At C#, the `static` callback code try to use the incoming `NativeTouchData` to find out which `Pad` was hit without help from `GraphicRaycaster`.
4. After found out which `Pad` was hit, on both iOS IL2CPP and Android Mono we directly use `NativeAudioPointer` on the correct `Pad` and call `nativeAudioPointer.Play()`. You are now playing Native Audio immediately in response of Native Touch even before letting the touch went to Unity's normal touch processing pipeline. On Android Mono we are in the other thread, but Native Audio could be used cross-thread so it is fine.
5. For Android IL2CPP where we disabled the slow callback we are relying on checking the "ring buffer" `NativeTouch.touches` in the main thread code.
6. Unlike the OFF ON case, it matters now how sooner in the frame we could execute the ring buffer checking and execute Native Audio playing, since Native Audio could play mid-frame. On Android, since the touch handling in Java is potentially in concurrent with Unity, we don't know when exactly this `NativeTouch.touches` will get written with new data. (It happens in real time! But you don't have to worry about race condition of reading from main thread at the same time as native writing it since I have put a mutex lock on it.)
7. As starter, we do like 7. of OFF ON case where we check the ring buffer on `MainLogic`'s `Update`. **But** instead of playing `AudioSource` on that `Pad` we instead use `nativeAudioPointer.Play()`. The audio now plays immediately.
8. But we are not stopping at that. From the reason in 6. we want to catch the touch as soon as possible without help from callback. `MainLogic`'s `Update` is one reliable place. But that means you would gain maximum latency reduction when the touch occurs and writted just before that `Update`. Unity's update loop goes according to [this page](https://docs.unity3d.com/Manual/ExecutionOrder.html), `EarlyUpdate`, `FixedUpdate`, `PreUpdate`, `Update`, `PreLateUpdate`, `PostLateUpdate` are pretty bunched up together, but `Initialization` phase is pretty far off in the beginning. (Especially if your game got some physics work which occurs before update) So, we are adding the same logic with `MainLogic`'s `Update` but placed at `Initialization` step instead. By the nature of `NativeTouch.touches`, it comes with "dequeue-style" helper to prevent duplicate read. So the two logic can be completely the same and whoever get it first can execute Native Audio playing. I think this 2 places are enough to get decent latency.