# Unity Native Drum Pad

** IN CONSTRUCTION !! **

This project show the lowest latency way I could think of in making an iOS/Android musical app in Unity.

Musical app do not just need fast audio playing, but also fast input processing because most audio is a result of user's interaction. In this project I show how to team up [Native Audio](http://exceed7.com/native-audio/) and [Native Touch](http://exceed7.com/native-touch) together to reduce both actual latency and perceived latency. It is quite involved but worth it in the end if you really care about this niche problem.

## Proof of concept

[Please see this video](https://www.youtube.com/watch?v=d6nx9eVHQeA).

As you can see it starts with nothing and the latency is unbearable. That's playing with `AudioSource`. When the grid turns red thats when Native Audio is enabled, you can see the audio get played about the same time as the pad lit up (because it was lit up by `EventTrigger`) then finally when Native Touch is on, you can hear that the audio is even ahead of the pad turning to orange. (Because Native Touch came earlier than `EventTrigger`, but I still use `EventTrigger` for visual response)

## Dependencies

Native Audio and Native Touch is a UPM dependency linked to local UPM in my own machine, as you can see in `manifest.json`.

```
    "com.e7.native-audio": "file:/Users/Sargon/Documents/Projects/NativeAudio/Assets/NativeAudio",
    "com.e7.native-touch": "file:/Users/Sargon/Documents/Projects/NativeTouch/Assets/NativeTouch",
```

If you want to try this out, you should comment out these 2 lines and import the package from Asset Store, or you can also link to Native Audio and Native Touch on your hard disk as local UPM like mine.

And then we have some Unity package dependencies, currently in preview : 

```
    "com.unity.burst": "1.0.0-preview.6",
    "com.unity.mathematics": "1.0.0-preview.1",
```

Together with C# jobs they allow us to speed up touch checking code further by scheduling a bursted parallel job to do an equivalent of `GraphicRaycaster` raycasting.

### Preprocessor directives

These 2 are defined in the Player Settings you can use : 

```
NATIVE_AUDIO; NATIVE_TOUCH; ANDROID_MONO
```

What if you only have Native Audio or only Native Touch? Or nothing at all? I have put preprocessors in the code so that they strip off the code that access my plugins. So if you have neither plugins you can still try out the project by removing both preprocessor, the project will compile and you will get a drum pad app played with purely Unity's default solution. (The check box on the side will disappear accordingly too!)

The project assume you are compiling Android on IL2CPP. Use `ANDROID_MONO` if you want to go Mono, and you will gain some extra performance. The reason is because Android IL2CPP couldn't do native to C# callback fast enough and the code is using an another slightly slower approach, but way faster than trying to use callback on IL2CPP, and maybe faster than fast callback on Mono if factoring overall script speed up from IL2CPP itself.

## Important project settings

- Minimum required version is 2018.3/2018.4LTS since the project used the new prefab workflow a bit.
- Recommended version to compile the project is Unity 2019.1 or higher. With 2018.3 you are putting some Android devices at unfair disadvantage when you turned off the Native Audio check box and try to compare latency. Because Unity's default audio playing got an upgrade in latency in 2019.1 making subset of devices get better native source. [You can read about it here](https://gametorrahod.com/unitys-android-audio-latency-improvement-in-2019-1-0-ebcffc31a947). (However Native Audio still wins on device that got the said treatment.)
- However if you decided to develop your real game locked at 2018.3/2018.4LTS and looking to compare how much Native Audio could speed up from Unity internal, compiling this project with those versions gives you better idea of that. 
- The project has Resolution Scaling activated and is scaled down a bit, to show that my Native Touch code works even with resolution scaling.
- The project also has the experimental Dynamic Resolution Scaling activated, just to flex that even the resolution changes in real time it still works.
- The Project Settings > Audio must be on "Best Latency". It doesn't affect Native Audio on Android but it does affect Native Audio on iOS, since the buffer size is shared in common for Unity too.

## Project walkthrough

- Each pad has its own `AudioSource`, `EventTrigger` on pointer down will play that source. The source is already connected with an `AudioClip`, so just `audioSource.Play()` will play the correct audio.
- Initialize both Native Audio and Native Touch. Native Audio will allocate native sources, Native Touch will register a `static` callback that the OS could call into C# from Java/Objective C immediately on touch.
- On `Awake` each pad also yank its `AudioClip` from the `AudioSource` to do `NativeAudio.Load(audioClip)`. The audio bytes are now copied to native side for fast playing.
- On `Start` each `RectTransform` of the `Pad` will be calculated to screen space `Rect`. `Rect` is a `struct` and allows us to use them safely from other thread (where the touch callback from OS might be) and also further boost checking performance with C# Jobs / ECS. This screen space is not from `Screen.width/height` API, but from `NativeTouch.RealScreenResoluion()`. Touches from the native side do not know if Unity is using (Dynamic) Resolution Scaling and is always relative to unscaled screen. This is to get ready ahead of time to be computed with touch from native (`NativeTouchData`) that Native Touch will give us.
- On screen rotation, the `RectTransform` will be recalculated. This app supports both portrait and landscape layout.

The behaviour of the app now depends on 2 `Toggle` on the side of the screen, used to turn on and off Native Audio and Native Touch and compare performance. I will explain them in details for educational purpose.

### Native Audio : OFF / Native Touch : OFF

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
9. Even with Native Touch, the pad is still relying on `EventTrigger`'s on pointer down to change its visual. The audio might had already played even before it lights up!

### Native Audio : **ON** / Native Touch : **ON**

1. On touch, at respective native side callback on native "view", but before the touch went into the closed source engine code, we write that touch into a "ring buffer" which C# could also read from.
2. Still in native side, if it is iOS (IL2CPP) **and** Android Mono where the callback speed is good, we are calling remembered C# delegate at this moment the touch happening. If it is Android IL2CPP where the callbck speed sucks, we set up Native Touch so that `noCallback` is enabled.
3. On iOS IL2CPP **and** Android Mono, At C#, the `static` callback code try to use the incoming `NativeTouchData` to find out which `Pad` was hit without help from `GraphicRaycaster`.
4. After found out which `Pad` was hit, on both iOS IL2CPP and Android Mono we directly use `NativeAudioPointer` on the correct `Pad` and call `nativeAudioPointer.Play()`. You are now playing Native Audio immediately in response of Native Touch even before letting the touch went to Unity's normal touch processing pipeline. On Android Mono we are in the other thread, but Native Audio could be used cross-thread so it is fine.
5. For Android IL2CPP where we disabled the slow callback we are relying on checking the "ring buffer" `NativeTouch.touches` in the main thread code.
6. Unlike the OFF ON case, it matters now how sooner in the frame we could execute the ring buffer checking and execute Native Audio playing, since Native Audio could play mid-frame. On Android, since the touch handling in Java is potentially in concurrent with Unity, we don't know when exactly this `NativeTouch.touches` will get written with new data. (It happens in real time! But you don't have to worry about race condition of reading from main thread at the same time as native writing it since I have put a mutex lock on it.)
7. As starter, we do like 7. of OFF ON case where we check the ring buffer on `MainLogic`'s `Update`. **But** instead of playing `AudioSource` on that `Pad` we instead use `nativeAudioPointer.Play()`. The audio now plays immediately.
8. But we are not stopping at that. From the reason in 6. we want to catch the touch as soon as possible without help from callback. `MainLogic`'s `Update` is one reliable place. But that means you would gain maximum latency reduction when the touch occurs and written just before that `Update`. Unity's update loop goes according to [this page](https://docs.unity3d.com/Manual/ExecutionOrder.html), `EarlyUpdate`, `FixedUpdate`, `PreUpdate`, `Update`, `PreLateUpdate`, `PostLateUpdate` are pretty bunched up together, but `Initialization` phase is pretty far off in the beginning. (Especially if your game got some physics work which occurs before update) So, we are adding the same logic with `MainLogic`'s `Update` but placed at `Initialization` step instead. By the nature of `NativeTouch.touches`, it comes with "dequeue-style" helper to prevent duplicate read. So the two logic can be completely the same and whoever get it first can execute Native Audio playing. I think these 2 places are enough to get decent latency.

## (Bonus) 4 classes of audio applications

Categorized unofficially by me, but might be useful.

### Music player

iTunes is a musical app. But do it need low latency play? No! No one cares if the song starts immediately on pressing play or not. The goodness is in the song and you are listening to it! In this case audio fidelity is the most important, and audio latency is not that of the concern. It is not an **interactive** audio application.

Also to prevent audio cracking because of buffer underrun they usually do so by increasing buffer size, which in turn increase latency. But music player do not care about latency, so that is generally a free quality.

### Sequencer

Application like digital audio workstation (DAW) on mobile phone or live performing musical apps like Looper, Launchpad falls into this category. The app **is interactive**, but the reference of what is the "correct" timing are all controllable. Imagine you start a drum loop. Each sound might have delay based on device, but all delays are equal, results in a perfect sequence albeit variable start time. When starting another loops, it is 100% possible for the software to compensate and match the beat that is currently playing. This class of application is immune to mobile audio latency.

### Instrument

Apps like GarageBand (in live playing mode) is in this category. The sound have to respond when you touch the screen. A latency can impact experience, but if you are rehearsing by yourself you might be able to ignore the latency since if you play perfectly, the output sound will all have equal latency and will be perfect with a bit of delay.

Also remember that **all** instruments in the world do have their own latency. Imagine a piano which the sound must travel through its chamber, or an effected guitar with rather long chain, or a vocalist which heard their own voice immediately "in the head". How could all of them managed to play together in sync at the concert? With practice you will get used to the latency naturally! So this class is not as bad as it sounds with a bit of latency.

### Music games

There are many music games on mobile phone made by Unity like Cytus, Deemo, Dynamix, VOEZ, Lanota, Arcaea, etc. If there is a sound feedback on hitting the note, this is the hardest class of the latency problem. Unlike Sequencer class, even though the song is predictable and the game know all the notes at all points in the song you cannot predict if the sound will play or not since it depends on player's performance. (Unless the sound is played regardless of hit or miss or bad judgement, then this class can be reduced to Sequencer class.)

It is harder than Instrument class, since now we have backing track playing as a reference and also a visual indicator. If you hit on time according to the visuals or music, you will get "Perfect" judgement but the sound will be off the backing track. When this happen, even though you get Perfect already you will automatically adapt to hit earlier to make that respond sound match with the song, in which case you will not get the Perfect judgement anymore. In the Instrument class, if you are live jamming with others this might happen too but if you adapt to hit early you can get accurate sound and not be punished by the judgement like in games.

Even a little bit of latency will be very obvious in a music game. Since there is a beat in the song for reference, players will be able to tell right away that he/she is hearing 2 separate sound (the beat in the song and the response sound) even if the player scores a perfect.

Also you can think music games works like instrument. No matter what the arcade machine will produce some latency. Game with respond sound like Beatmania IIDX, Groove Coaster, or DJMAX Respect for example if you listen to the "button sound" of good players that tapped to the music, the tap sound is obviously **before** the audio, but they could consistently do that throughout the song that the response sound comes out perfectly in sync and get perfect scores. These games in a way, works like a real instrument. **You live with the latency** and get good.

On the contrary a game like Dance Dance Revolution has no response sound, and is properly calibrated so that you can step **exactly** to the music and get Marvelous judgement. If you go listen to footstep of good players, you can hear that it matches perfectly with the audio. In effect that means a game like this had already accounted for the time audio traveled from loudspeaker to your ear in the judgement calibration!

This drum pad app belongs in "Instrument" category. I think the latency is good enough for live jamming. (But may need better sound set... sorry for that.)