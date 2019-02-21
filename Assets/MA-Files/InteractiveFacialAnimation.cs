using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;




/*
 * Maya Game Exporter kannste knicken, buggt nur rum.
 */
[RequireComponent(typeof(Animator))]
public class InteractiveFacialAnimation : MonoBehaviour
{
    // A mask should be giving in order to avoid overriding body animation
    public AvatarMask headMask;

    // Public Data Structures
    [HideInInspector]
    public List<Emotion> emotionObjects = new List<Emotion>();  // Contains all Clips
    [HideInInspector]
    public List<GoToEmotionEntry> goToEmotionList;              // Contains emotion names and whether they should be transitioned to 

    // For display purposes
    public GameObject GOWithDrawGraphOnImage;
    DrawGraphOnImage drawGraphOnImage;

    // To select Interpolation modes through the inspector
    public enum interpolationENUM { Linear, Cubic, Bezier };
    public interpolationENUM interpolationMode;
    public float pBezierP1 = 0.7f;
    public float pBezierP2 = 0.3f;

    // Start Emotion
    public string startEmotion = "Neutral";

    // Flag for a hack that ought to fix the "stuck blendshapes" bug of the Playables API. Can and WILL break other scripts (such as the LookAt script)!
    public bool HACKFixStuckBlendshapes = false;

    // Private Data Structures
    private Dictionary<string, int> playablesDict;  // Find the correct PlayableInputID for a given animation clip
    private Queue<int> goToEmotionNext = new Queue<int>();  // Store transition calls to play them in FIFO order

    // Animators
    private Animator animator;
    private RuntimeAnimatorController runtimeAnimController;

    // Playables
    private PlayableGraph playableGraph;
    private AnimationMixerPlayable mixerEmotionPlayable;

    // Flags for Update
    private bool fPlayTransition = false;
    private bool fInitTransition = true;
    private bool fPlayMainAfterTransition;
    private bool fWithTransitionIn;
    private bool fPlayMain;
    private bool smoothMainClips = true;
    private bool normalize = false;
    private bool fPlayLoopMain = false;
    private bool fInitLoopMain = true;

    // Persisent Variables for Update
    private string currentlyPlaying;                // The currently playing emotion
    private string transitionEmotion;               // Emotion transitioning to
    private float lerpBlendDuration = 0.5f;         // How long a transition should take
    private float currentTime = 0f;                 // How much transition time has already elapsed
    private int emotionNumber = 0;                  // Which clips to play for a given emotion
    private int previousEmotionNumber = 0;
    private string blendToKey;

    private int mainMixerMainIndex = 0;
    private int mainMixerCopyIndex = 1;
    private float mainLoopTriggerTime = 1.0f;              // Time over which a main loop is interpolated with itself. [TODO] Should be made relative to main clip duration, as it will behave strangely for small durations.
    private float mainLoopCurrentTime = 0f;
    private string mainBlendToKey;




    /*
     * Awake
     */
    private void Awake()
    {
        animator = GetComponent<Animator>();
        goToEmotionList = new List<GoToEmotionEntry>();

        // Create Playable Graph
        playableGraph = PlayableGraph.Create("ClairePlayableGraph");
        playableGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        var playableOutput = AnimationPlayableOutput.Create(playableGraph, "Animation", animator);

        // Create Top Level Layer Mixer
        AnimationLayerMixerPlayable mixerLayerPlayable = AnimationLayerMixerPlayable.Create(playableGraph, 2);
        playableOutput.SetSourcePlayable(mixerLayerPlayable);

        // Create an Emotion Mixer
        int numberOfClips = 0;
        for (int i = 0; i < emotionObjects.Count; i++)
        {
            for (int j = 0; j < emotionObjects[i].animationGroupList.Count; j++)
            {
                if (emotionObjects[i].animationGroupList[j].main) numberOfClips++;
                if (emotionObjects[i].animationGroupList[j].transitionIn) numberOfClips++;
            }
        }
        mixerEmotionPlayable = AnimationMixerPlayable.Create(playableGraph, numberOfClips);    // Second argument sets number of inputs for clips to connect.

        // Wrap AnimController
        runtimeAnimController = animator.runtimeAnimatorController;
        var runtimeAnimControllerPlayable = AnimatorControllerPlayable.Create(playableGraph, runtimeAnimController);

        // Connect to Top Level Layer Mixer
        playableGraph.Connect(runtimeAnimControllerPlayable, 0, mixerLayerPlayable, 0);
        playableGraph.Connect(mixerEmotionPlayable, 0, mixerLayerPlayable, 1);
        mixerLayerPlayable.SetInputWeight(0, 1.0f);
        mixerLayerPlayable.SetInputWeight(1, 1.0f);
        mixerLayerPlayable.SetLayerMaskFromAvatarMask(1, headMask);
        //mixerLayerPlayable.SetLayerAdditive(1, true);

        // 1. Wraps each clip in a playable and connects them to the emotion mixer
        // 2. Also populate "playablesDict" to later be able to access the AnimationClipPlayables by their Index in the emotionMixer
        // 3. Main animations are not connected directly: Instead another MixerPlayable (mainMixer) is connected.
        //    The main playable is copied and both the original and copy are connected to the new MainMixer.
        //    This is to allow for smoothly looping main animations
        playablesDict = new Dictionary<string, int>();      // String: "Name"+"Index of Emotion Variation"; Int: Index in mixerEmotionPlayable
        int playablesCount = 0;
        int tempPlayablesCount = 0;
        for (int i = 0; i < emotionObjects.Count; i++)
        {
            tempPlayablesCount = playablesCount;
            for (int j = 0; j < emotionObjects[i].animationGroupList.Count; j++)
            {
                if (emotionObjects[i].animationGroupList[j].main)
                {
                    playablesDict.Add(emotionObjects[i].name + j, playablesCount);
                    AnimationMixerPlayable mainMixer = AnimationMixerPlayable.Create(playableGraph, 2);
                    playableGraph.Connect(mainMixer, 0, mixerEmotionPlayable, playablesCount);
                    playablesCount++;

                    AnimationClipPlayable main = AnimationClipPlayable.Create(playableGraph, emotionObjects[i].animationGroupList[j].main);
                    playableGraph.Connect(main, 0, mainMixer, mainMixerMainIndex);
                    mainMixer.SetInputWeight(mainMixerMainIndex, 1.0f);  // Set first clip to active
                    main.SetDuration(emotionObjects[i].animationGroupList[j].main.length);

                    AnimationClipPlayable mainCopy = AnimationClipPlayable.Create(playableGraph, emotionObjects[i].animationGroupList[j].main);
                    playableGraph.Connect(mainCopy, 0, mainMixer, mainMixerCopyIndex);
                    mainMixer.SetInputWeight(mainMixerCopyIndex, 0.1f);  // Set second clip to inactive
                    mainCopy.SetDuration(emotionObjects[i].animationGroupList[j].main.length);
                }

                if (emotionObjects[i].animationGroupList[j].transitionIn)
                {
                    playablesDict.Add(emotionObjects[i].name + "TransitionIn" + j, playablesCount);
                    AnimationClipPlayable transition = AnimationClipPlayable.Create(playableGraph, emotionObjects[i].animationGroupList[j].transitionIn);
                    playableGraph.Connect(transition, 0, mixerEmotionPlayable, playablesCount);

                    transition.SetDuration(emotionObjects[i].animationGroupList[j].transitionIn.length);
                    playablesCount++;
                }
            }

            // If an emotion has any animationClips, it can be transitioned to
            if (tempPlayablesCount < playablesCount)
            {
                goToEmotionList.Add(new GoToEmotionEntry(emotionObjects[i].name, false, i));
            }
        }

        // Unrelated to Playables; For Display Purposes
        if (GOWithDrawGraphOnImage)
            drawGraphOnImage = GOWithDrawGraphOnImage.GetComponent<DrawGraphOnImage>();
    }

    /*
     * Start
     */
    void Start()
    {
        // Set starting to either given startEmotion or default to the first in the list
        if (!playEmotion(startEmotion))
        {
            Debug.LogError("Given starting emotion does not exist.");
            currentlyPlaying = emotionObjects[goToEmotionList[0].indexInEmotionObjects].name;  // If not given, assume some default value
            goToEmotionNext.Enqueue(goToEmotionList[0].indexInEmotionObjects);
        }
        else
        {
            currentlyPlaying = startEmotion;
        }

        // Plays the Playables Graph, i.e. starts playing animations
        playableGraph.Play();
    }

    /*
     * Update
     */
    void LateUpdate()
    {
        if (HACKFixStuckBlendshapes)
        {
            animator.runtimeAnimatorController = null;          // Necessary to fix a bug where blendshapes "get stuck" on SetInputWeight changes. Reassigned at the end of Update.
        }

        // Check if new Transitions were requested and add them to the queue
        checkForNewTransitions();

        // If no transition is playing, prepare next transition
        if (!fPlayTransition && goToEmotionNext.Count > 0)
        {
            prepareNextTransition();
        }

        // Play Transition
        if (fPlayTransition)
        {
            playTransition();
        }

        // Play main emotion slightly before transition ends to avoid the 1 'neutral' frame that occurs when using "isDone()"
        if (fPlayMainAfterTransition && ((mixerEmotionPlayable.GetInput(playablesDict[transitionEmotion + "TransitionIn" + emotionNumber]).GetDuration() - mixerEmotionPlayable.GetInput(playablesDict[transitionEmotion + "TransitionIn" + emotionNumber]).GetTime()) < 0.1)) //(mixerEmotionPlayable.GetInput(1).GetTime() >= mixerEmotionPlayable.GetInput(1).GetDuration())
        {
            playMainAfterTransition();
        }

        // If close to the end of a main clip, set loop starting flag
        if (!fPlayTransition && !currentlyPlaying.Contains("TransitionIn"))
        {
            if (((mixerEmotionPlayable.GetInput(playablesDict[currentlyPlaying + emotionNumber]).GetInput(mainMixerMainIndex).GetDuration() - mixerEmotionPlayable.GetInput(playablesDict[currentlyPlaying + emotionNumber]).GetInput(mainMixerMainIndex).GetTime()) < mainLoopTriggerTime))
            {
                fPlayLoopMain = true;
            }
        }

        // Ensure a smooth loop to the beginning for the main animation. Based on linearly interpolating the running clip with a copy of itself.
        if (fPlayLoopMain)
        {
            playLoopMainToStart();
        }

        // If weights were changed, normalize them
        if (normalize) normalizeWeights();

        if (HACKFixStuckBlendshapes)
            animator.runtimeAnimatorController = runtimeAnimController;

        //Debug.Log("Happy Wieght: " + mixerEmotionPlayable.GetInputWeight(0));
        //Debug.Log("Angry Wieght: " + mixerEmotionPlayable.GetInputWeight(2));
        //Debug.Log("TPose Wieght: " + mixerEmotionPlayable.GetInputWeight(4));
    }

    private void checkForNewTransitions()
    {
        for (int i = 0; i < goToEmotionList.Count; i++)
        {
            if (goToEmotionList[i].goToEmotion)
            {
                Debug.Log("New Transition found");
                goToEmotionNext.Enqueue(goToEmotionList[i].indexInEmotionObjects);
                goToEmotionList[i].goToEmotion = false;
            }
        }
    }

    private void prepareNextTransition()
    {
        transitionEmotion = emotionObjects[goToEmotionNext.Dequeue()].name;
        fPlayTransition = true;

        // Randomly select one of the clips sets of the emotion
        for (int j = 0; j < emotionObjects.Count; j++)
        {
            if (emotionObjects[j].name.Equals(transitionEmotion))
            {
                emotionNumber = Random.Range(0, emotionObjects[j].animationGroupList.Count);
            }
        }

        // Flag wether there is a transitionIn animation or not
        if (playablesDict.ContainsKey(transitionEmotion + "TransitionIn" + emotionNumber))
        {
            fWithTransitionIn = true;
        }
        else
        {
            fWithTransitionIn = false;
        }

        // Avoid transitioning to the same emotion if there is no TransitionIn animation
        if (!fWithTransitionIn && (transitionEmotion.Equals(currentlyPlaying)))
        {
            fPlayTransition = false;
        }

        // Draw for display purposes
        if (GOWithDrawGraphOnImage)
            drawGraphOnImage.clear();
    }


    private void playTransition()
    {
        // Initialize Transition when it begins
        if (fInitTransition)
        {
            if (fWithTransitionIn)
            {
                blendToKey = transitionEmotion + "TransitionIn" + emotionNumber;
                mixerEmotionPlayable.GetInput(playablesDict[blendToKey]).SetTime(0f);
                mixerEmotionPlayable.GetInput(playablesDict[blendToKey]).SetDone(false);
            }
            else
            {
                blendToKey = transitionEmotion + emotionNumber;
                mixerEmotionPlayable.GetInput(playablesDict[blendToKey]).GetInput(mainMixerMainIndex).SetTime(0f);
                mixerEmotionPlayable.GetInput(playablesDict[blendToKey]).GetInput(mainMixerMainIndex).SetDone(false);
            }

            fInitTransition = false;
            fPlayMainAfterTransition = false;   // In case a new transition starts while a transitionIn animation is still playing
        }

        // LERP Transition and currently playing emotion
        // Interpolation website: http://paulbourke.net/miscellaneous/interpolation/
        currentTime += Time.deltaTime;
        float mu = currentTime / lerpBlendDuration;
        float t;
        float upcomingBlendWeight = 0;
        float pStart = 0f;
        float pEnd = 1f;

        switch (interpolationMode)
        {
            case interpolationENUM.Linear:
                upcomingBlendWeight = myLerp(pStart, pEnd, mu, interpolationENUM.Linear);
                break;
            case interpolationENUM.Cubic:
                upcomingBlendWeight = myLerp(pStart, pEnd, mu, interpolationENUM.Cubic);
                break;
            case interpolationENUM.Bezier:
                upcomingBlendWeight = myLerp(pStart, pEnd, mu, interpolationENUM.Bezier, pBezierP1, pBezierP2);
                break;
            default:
                break;
        }

        // Draw for display purposes
        if (GOWithDrawGraphOnImage)
        {
            drawGraphOnImage.drawPoint(0, pStart, Color.red);
            drawGraphOnImage.drawPoint(1, pEnd, Color.green);
            drawGraphOnImage.drawPoint(mu, upcomingBlendWeight, Color.black);
        }


        // Set Blend Weights
        mixerEmotionPlayable.SetInputWeight(playablesDict[currentlyPlaying + previousEmotionNumber], 1f - upcomingBlendWeight);
        mixerEmotionPlayable.SetInputWeight(playablesDict[blendToKey], upcomingBlendWeight);

        // End Transition CleanUp, Prepare playing Main
        if (currentTime >= lerpBlendDuration)
        {
            Debug.Log("Transition from " + currentlyPlaying + previousEmotionNumber + " to " + blendToKey + " complete.");

            if (fWithTransitionIn)
            {
                fPlayMainAfterTransition = true;
                currentlyPlaying = transitionEmotion + "TransitionIn";
            }
            else
            {
                currentlyPlaying = transitionEmotion;
            }

            fPlayTransition = false;
            fInitTransition = true;
            currentTime = 0;
            previousEmotionNumber = emotionNumber;
        }
        normalize = true;
    }

    private void playMainAfterTransition()
    {
        currentlyPlaying = transitionEmotion;
        fPlayMainAfterTransition = false;
        mixerEmotionPlayable.SetInputWeight(playablesDict[currentlyPlaying + "TransitionIn" + emotionNumber], 0.0f);   // Deactivate Transition
        mixerEmotionPlayable.SetInputWeight(playablesDict[currentlyPlaying + emotionNumber], 1.0f);                    // Active Main
        mixerEmotionPlayable.GetInput(playablesDict[currentlyPlaying + emotionNumber]).GetInput(mainMixerMainIndex).SetTime(0f);
        normalize = true;
    }

    private void playLoopMainToStart()
    {
        // Init
        if (fInitLoopMain)
        {
            mainBlendToKey = currentlyPlaying + emotionNumber;
            mixerEmotionPlayable.GetInput(playablesDict[mainBlendToKey]).GetInput(mainMixerCopyIndex).SetTime(0f);
            fInitLoopMain = false;
        }

        // LERP Init
        mainLoopCurrentTime += Time.deltaTime;
        float mu = mainLoopCurrentTime / mainLoopTriggerTime;
        float t;
        float upcomingBlendWeight = 0;
        float pStart = 0f;
        float pEnd = 1f;

        // Linear
        upcomingBlendWeight = myLerp(pStart, pEnd, mu, interpolationENUM.Linear);

        // Set Weights
        mixerEmotionPlayable.GetInput(playablesDict[mainBlendToKey]).SetInputWeight(mainMixerMainIndex, 1f - upcomingBlendWeight);
        mixerEmotionPlayable.GetInput(playablesDict[mainBlendToKey]).SetInputWeight(mainMixerCopyIndex, upcomingBlendWeight);


        // CleanUp
        if (upcomingBlendWeight >= mainLoopTriggerTime)
        {
            float timeElapsed = (float)mixerEmotionPlayable.GetInput(playablesDict[mainBlendToKey]).GetInput(mainMixerCopyIndex).GetTime();
            mixerEmotionPlayable.GetInput(playablesDict[mainBlendToKey]).GetInput(mainMixerMainIndex).SetTime(timeElapsed);
            mixerEmotionPlayable.GetInput(playablesDict[mainBlendToKey]).SetInputWeight(mainMixerMainIndex, 1.0f);
            mixerEmotionPlayable.GetInput(playablesDict[mainBlendToKey]).SetInputWeight(mainMixerCopyIndex, 0.0f);

            mainLoopCurrentTime = 0.0f;
            fInitLoopMain = true;
            fPlayLoopMain = false;
        }

    }



    // Normalize Weights in mixerEmotionPlayable
    private void normalizeWeights()
    {
        int length = mixerEmotionPlayable.GetInputCount();
        float sumOfWeights = 0;
        for (int i = 0; i < length; i++)
        {
            if (mixerEmotionPlayable.GetInputWeight(i) > 0f) sumOfWeights += mixerEmotionPlayable.GetInputWeight(i);
        }
        for (int i = 0; i < length; i++)
        {
            if (mixerEmotionPlayable.GetInputWeight(i) > 0f)
            {
                mixerEmotionPlayable.SetInputWeight(i, mixerEmotionPlayable.GetInputWeight(i) / sumOfWeights);
            }
        }
        normalize = false;
    }


    /*
     * A lerping function that goes beyond interpolation by linear functions.
     */
    private float myLerp(float startPoint, float endPoint, float interpolation, interpolationENUM interpolationMode, float bezierP1 = 0.3f, float bezierP2 = 0.7f)
    {
        Debug.Log("BP1: " + bezierP1 + " BP1: " + bezierP2);
        float result;
        float t;

        switch (interpolationMode)
        {
            case interpolationENUM.Linear:
                t = interpolation;
                result = Mathf.Lerp(startPoint, endPoint, t);
                break;

            case interpolationENUM.Cubic:
                t = (1 - Mathf.Cos(interpolation * Mathf.PI)) / 2;
                result = Mathf.Lerp(startPoint, endPoint, t);
                break;

            case interpolationENUM.Bezier:
                // Bezier, B-Spline? 4-point interpolation, so they need two manually defined points. No recommendations?
                // S. 148, kurze Erwänung unter Nonlinear Interpolation: https://books.google.de/books?id=yEzrBgAAQBAJ&pg=PA147&lpg=PA147&dq=facial+animation+transition+interpolation&source=bl&ots=ntfbn7k6ww&sig=FDfcPAnj_mr76FcedSKEx4zrjRU&hl=de&sa=X&ved=2ahUKEwi_u42K3q7fAhUHmYsKHfYJDncQ6AEwA3oECAcQAQ#v=onepage&q=facial%20animation%20transition%20interpolation&f=false
                // Linear, Cubic B-Spline, Cardinal Spline: https://www.researchgate.net/publication/44250675_Parametric_Facial_Expression_Synthesis_and_Animation
                // Best Bezier explanation ever: https://denisrizov.com/2016/06/02/bezier-curves-unity-package-included/
                t = interpolation;

                float u = 1f - t;
                float t2 = t * t;
                float u2 = u * u;
                float u3 = u2 * u;
                float t3 = t2 * t;

                result =
                    (u3) * startPoint +
                    (3f * u2 * t) * bezierP1 +
                    (3f * u * t2) * bezierP2 +
                    (t3) * endPoint;
                break;

            default:
                result = 0;
                break;

        }
        return result;
    }


    /*
     * EDITOR MODE ONLY! 
     * Used by PlayablesPrototypeV2 Controller to assign new emotions and remove old ones.
     */
    public void updateEmotionList(List<string> newEmotions)
    {
        // Add new emotions
        foreach (string emotion in newEmotions)
        {
            bool contains = false;
            for (int i = 0; i < emotionObjects.Count; i++)
            {
                if (emotionObjects[i].name.Equals(emotion)) contains = true;
            }
            if (!contains)
            {
                Debug.Log("Emotion: " + emotion);
                emotionObjects.Add(new Emotion(emotion));
            }
        }
        // Delete removed emotions
        for (int i = 0; i < emotionObjects.Count; i++)
        {
            if (!newEmotions.Contains(emotionObjects[i].name))
            {
                emotionObjects.Remove(emotionObjects[i]);
                i--;
            }
        }

        /*
        goToEmotionList = new List<StringBool>();
        foreach (string emotion in newEmotions)
        {
            goToEmotionList.Add(new StringBool(emotion, false));
        }
        */
    }


    /*
     * Adds a new emotion to the transition queue.
     * Input nextEmotion: The emotion that will be transitioned to.
     * Return: Returns true if emotion was added to the transition queue, else false.
     */
    public bool playEmotion(string nextEmotion)
    {
        for (int i = 0; i < goToEmotionList.Count; i++)
        {
            if (goToEmotionList[i].name.Equals(nextEmotion))
            {
                goToEmotionNext.Enqueue(goToEmotionList[i].indexInEmotionObjects);
                return true;
            }
        }
        return false;
    }

    /*
     * Getter and Setter
     */
    public AvatarMask getAvatarMask()
    {
        return headMask;
    }
    public void setAvatarMask(AvatarMask avatarMask)
    {
        headMask = avatarMask;
    }
    public interpolationENUM getInterpolationMode()
    {
        return interpolationMode;
    }
    public void setInterpolationMode(interpolationENUM interpol)
    {
        interpolationMode = interpol;
    }
    public bool getHACKFixStuckBlendshapes()
    {
        return HACKFixStuckBlendshapes;
    }
    public void setHACKFIXStuckBlendshapes(bool setTo)
    {
        HACKFixStuckBlendshapes = setTo;
    }
    public string getStartEmotion()
    {
        return startEmotion;
    }
    public void setStartEmotion(string newStartEmotion)
    {
        startEmotion = newStartEmotion;
    }

    void OnDisable()
    {
        // Destroys all Playables and PlayableOutputs created by the graph.
        playableGraph.Destroy();
    }
}

/*
 * A helper class to properly display GoTo options in the editor
 */
[System.Serializable]
public class GoToEmotionEntry
{
    public string name;
    public bool goToEmotion;
    public int indexInEmotionObjects;

    public GoToEmotionEntry(string name, bool goToEmotion, int indexInEmotionObjects)
    {
        this.name = name;
        this.goToEmotion = goToEmotion;
        this.indexInEmotionObjects = indexInEmotionObjects;
    }
}