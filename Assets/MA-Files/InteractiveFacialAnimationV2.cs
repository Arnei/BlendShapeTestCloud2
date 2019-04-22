using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;




/*
 * Maya Game Exporter kannste knicken, buggt nur rum.
 */
[RequireComponent(typeof(Animator))]
public class InteractiveFacialAnimationV2 : MonoBehaviour
{
    [Header("Fill In Components")]
    [Tooltip("A mask masking everything but the characters head in order to avoid accidentally overriding body animation")]
    public AvatarMask headMask;

    // Public Data Structures
    [HideInInspector]
    public List<Emotion> emotionObjects = new List<Emotion>();  // Contains all Clips
    [HideInInspector]
    public List<GoToEmotionEntry> goToEmotionList;              // Contains emotion names and whether they should be transitioned to 

    // For display purposes
    [Tooltip("Optional: A gameobject with the DrawGraphOnImage component attached. Draws interpolation onto a GUI Image.")]
    public GameObject GOWithDrawGraphOnImage;
    DrawGraphOnImage drawGraphOnImage;


    // To select Interpolation modes through the inspector
    public enum interpolationENUM { Linear, Cubic, Bezier };
    [Header("Settings")]
    [Tooltip("Select the preferred interpolation mode.")]
    public interpolationENUM interpolationMode;
    [Tooltip("For Bezier interpolation. Sets the Y coordinate for (0,Y)")]
    public float pBezierP1 = 0.7f;
    [Tooltip("For Bezier interpolation. Sets the Y coordinate for (1,Y)")]
    public float pBezierP2 = 0.3f;

    [Tooltip("Set emotion that starts playing when playing the scene.")]
    public string startEmotion = "Neutral";

    // Public Flags
    [Tooltip("Flag for a hack that ought to fix the 'stuck blendshapes' bug of the Playables API. Can and WILL break other scripts (such as the LookAt script)!")]
    public bool HACKFixStuckBlendshapes = false;
    [Header("Automatic Switching Settings")]
    [Tooltip("Switches between clips in an Emotion, if that emotion has more than one clip.")]
    public bool switchBetweenTracks = true;             // There is most certainly a smarter way to go about this. TODO: Make it smarter
    [Tooltip("The probability with which a switch will occur. Ought to be set between 0 and switchingProbabilityMax. Is checked for every frame, so should be set rather low.")]
    public int switchingProbability = 1;
    [Tooltip("The max value for the probability range. Minimum is 0")]// 
    public int switchingProbabilityMax = 10000;         // 


    // Private Data Structures
    private Dictionary<string, int> playablesDict;      // Find the correct PlayableInputID for a given animation clip
    private Queue<NextEmotion> goToEmotionNext = new Queue<NextEmotion>();  // Store transition calls to play them in FIFO order

    // Animators
    private Animator animator;
    private RuntimeAnimatorController runtimeAnimController;

    // Playables
    private PlayableGraph playableGraph;
    private AnimationMixerPlayable mixerEmotionPlayable;

    // Flags for Update
    private bool fBlending = false;                     // Whether the script is in Playing or Blending Mode
    private bool normalize = false;

    // Persisent Variables for Update
    //private string currentlyPlaying;                  // UNUSED, but may be later required again. The currently playing emotion
    private List<IFAClip> currentlyPlayingClips;        // Keep track of clips with a weight > 0 to A. update them and B. Allow for proper transitions
    private IFABlender blender;                         // Variable storing the currently active blending instance
    private string transitionEmotion;                   // Emotion transitioning to
    private float lerpBlendDuration = 0.5f;             // How long a transition should take
    private int emotionNumber = 0;                      // Which clips to play for a given emotion
    private int emotionNumberCount;                     // Count of total available emotionNumbers aside from the current emotionNumber


    private const int mainMixerMainIndex = 0;           // Indices for the mainMixer Variable
    private const int mainMixerCopyIndex = 1;
    private float mainLoopTriggerTime = 1.0f;           // Time over which a main loop is interpolated with itself. [TODO] Should be made relative to main clip duration, as it will behave strangely for small durations.

    /*
     * Awake
     */
    private void Awake()
    {
        animator = GetComponent<Animator>();
        goToEmotionList = new List<GoToEmotionEntry>();

        currentlyPlayingClips = new List<IFAClip>();

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
                    //var mainMixerBehaviour = ScriptPlayable<IFAMainMixerBehaviour>.Create(playableGraph, 1);                                    // Throwing a behaviour controller between emotionMixer and mainMixer
                    //mainMixerBehaviour.GetBehaviour().constructor2(mainMixer, mainLoopTriggerTime, mainMixerMainIndex, mainMixerCopyIndex);     // To automize looping of the main clip
                    //playableGraph.Connect(mainMixerBehaviour, 0, mixerEmotionPlayable, playablesCount);
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
        bool givenStartEmotionExists = false;
        for (int i = 0; i < goToEmotionList.Count; i++)
        {
            if (goToEmotionList[i].name.Equals(startEmotion))
            {
                givenStartEmotionExists = true;
            }
        }

        if (!givenStartEmotionExists)
        {
            Debug.LogError("Given starting emotion does not exist.");
            //currentlyPlaying = emotionObjects[goToEmotionList[0].indexInEmotionObjects].name;  // If not given, assume some default value
            IFAClip startingClip = new IFAClip(playablesDict, mixerEmotionPlayable, emotionObjects[goToEmotionList[0].indexInEmotionObjects].name + "0", true);
            startingClip.setWeight(1.0f);
            currentlyPlayingClips.Add(startingClip);
        }
        else
        {
            //currentlyPlaying = startEmotion;
            IFAClip startingClip = new IFAClip(playablesDict, mixerEmotionPlayable, startEmotion + "0", true);
            startingClip.setWeight(1.0f);
            currentlyPlayingClips.Add(startingClip);
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

        // If there is a blending request, prepare next blending
        if (goToEmotionNext.Count > 0)
        {
            // Get the next 
            NextEmotion nextEmotion = goToEmotionNext.Dequeue();
            transitionEmotion = emotionObjects[nextEmotion.indexInEmotionObjects].name;
            fBlending = true;

            // Randomly select one of the clips sets of the emotion
            for (int j = 0; j < emotionObjects.Count; j++)
            {
                if (emotionObjects[j].name.Equals(transitionEmotion))
                {
                    emotionNumber = Random.Range(0, emotionObjects[j].animationGroupList.Count);
                    emotionNumberCount = emotionObjects[j].animationGroupList.Count;                // Later used in the switchBetweenTracks part
                }
            }
            // Or if a specific clip is requested, select that
            if(nextEmotion.emotionNumber != -1)
                emotionNumber = nextEmotion.emotionNumber;

            // Get the clips that were last active in the last blending process, in case this blending interrupts the previous one
            if (blender != null)
                blender.addActiveClips(currentlyPlayingClips);

            // Flag wether there is a transitionIn animation or not
            // Instantiate containers appropriately
            if (playablesDict.ContainsKey(transitionEmotion + "TransitionIn" + emotionNumber) && nextEmotion.withTransitionInIfPossible)
            {
                IFAClip transitionClip = new IFAClip(playablesDict, mixerEmotionPlayable, transitionEmotion + "TransitionIn" + emotionNumber, false);
                IFAClip nextEmotionClip = new IFAClip(playablesDict, mixerEmotionPlayable, transitionEmotion + emotionNumber, true);
                blender = new IFABlenderTransition(currentlyPlayingClips, transitionClip, nextEmotionClip, interpolationMode.ToString(), drawGraphOnImage);
            }
            else
            {
                IFAClip nextEmotionClip = new IFAClip(playablesDict, mixerEmotionPlayable, transitionEmotion + emotionNumber, true);
                blender = new IFABlenderMain(currentlyPlayingClips, nextEmotionClip, interpolationMode.ToString(), drawGraphOnImage);
            }

            // Draw for display purposes
            if (GOWithDrawGraphOnImage)
                drawGraphOnImage.clear();
        }

        // Main Part
        if (!fBlending)
        {
            // Call all functions that need to be called each frame 
            foreach (IFAClip clip in currentlyPlayingClips)
            {
                clip.update();
            }

            if(switchBetweenTracks)
            {
                // Randomly queue a transition to another clip within the same emotion, making sure it is no the same clip
                if (Random.Range(0, switchingProbabilityMax) < switchingProbability && 
                    emotionNumberCount > 1 && 
                    goToEmotionNext.Count == 0)
                {
                    // Get a random number that is not the old number
                    int newEmotionNumber = Random.Range(0, emotionNumberCount - 1);
                    if (newEmotionNumber >= emotionNumber)
                        newEmotionNumber++;
                    // Queue the transition for the next frame
                    playEmotion(transitionEmotion, newEmotionNumber, false);
                }
            }
        }
        // Blending Part
        if (fBlending)
        {
            // Call all functions that need to be called each frame 
            blender.update();

            // If Blending has finished, clean up
            if(blender.isBlendingDone())
            {
                Debug.Log("Blending is done");
                fBlending = false;

                // Update currentlyPlayingClips
                blender.addActiveClips(currentlyPlayingClips);

                // Remove dead clips from currentlyPlayingClips
                List<int> tempToRemove = new List<int>();
                for (int i = currentlyPlayingClips.Count - 1; i >= 0; i--)
                {
                    if (currentlyPlayingClips[i].getWeight() <= 0)
                    {
                        tempToRemove.Add(i);
                    }
                }
                foreach (int index in tempToRemove)
                {
                    Debug.Log("Removed: " + currentlyPlayingClips[index].GetType());
                    currentlyPlayingClips.RemoveAt(index);
                }
            }
        }

        // If weights were changed, normalize them
        //if (normalize) normalizeWeights();
        normalizeWeights();

        if (HACKFixStuckBlendshapes)
            animator.runtimeAnimatorController = runtimeAnimController;

        //Debug.Log("Happy Wieght: " + mixerEmotionPlayable.GetInputWeight(0));
        //Debug.Log("Angry Wieght: " + mixerEmotionPlayable.GetInputWeight(2));
        //Debug.Log("TPose Wieght: " + mixerEmotionPlayable.GetInputWeight(4));
    }

    /*
     * Checks if any Transitions were requested through the inspector and if so, puts them in the goToEmotionNext queue
     * Only queues one possible transition per call
     */
    private void checkForNewTransitions()
    {
        for (int i = 0; i < goToEmotionList.Count; i++)
        {
            if (goToEmotionList[i].goToEmotion)
            {
                Debug.Log("New Transition found");
                goToEmotionNext.Enqueue(new NextEmotion(goToEmotionList[i].indexInEmotionObjects));
                goToEmotionList[i].goToEmotion = false;
            }
        }
    }


    /*
     * Normalize Weights in mixerEmotionPlayable
     */
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
    }


    /*
     * Adds a new emotion to the transition queue.
     * Input nextEmotion: The emotion that will be transitioned to.
     *       nextEmotionNumber: The number of the exact clip within the emotion (-1==Random)
     *       withTransitionIn: Whether a transitionIn animation should be played
     * Return: Returns true if emotion was added to the transition queue, else false.
     */
    public bool playEmotion(string nextEmotion)
    {
        for (int i = 0; i < goToEmotionList.Count; i++)
        {
            if (goToEmotionList[i].name.Equals(nextEmotion))
            {
                goToEmotionNext.Enqueue(new NextEmotion(goToEmotionList[i].indexInEmotionObjects));
                return true;
            }
        }
        return false;
    }
    public bool playEmotion(string nextEmotion, int nextEmotionNumber, bool withTransitionIn)
    {
        for (int i = 0; i < goToEmotionList.Count; i++)
        {
            if (goToEmotionList[i].name.Equals(nextEmotion))
            {
                goToEmotionNext.Enqueue(new NextEmotion(goToEmotionList[i].indexInEmotionObjects, nextEmotionNumber, withTransitionIn));
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
 *
 /*
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
*/

/*
 * Struct to accumulate possible parameters for the next transition
 */
public class NextEmotion
{
    public int emotionNumber;                   // The index number of the specific clip(-pair) in an Emotion
    public int indexInEmotionObjects;           // The index of the emotion in EmotionObjects
    public bool withTransitionInIfPossible;     // Whether a transitionIn animation should be played, if it exists

    public NextEmotion(int indexInEmotionObjects)
    {
        this.indexInEmotionObjects = indexInEmotionObjects;
        this.emotionNumber = -1;
        withTransitionInIfPossible = true;
    }

    public NextEmotion(int indexInEmotionObjects, int emotionNumber, bool withTransitionIn)
    {
        this.indexInEmotionObjects = indexInEmotionObjects;
        this.emotionNumber = emotionNumber;
        this.withTransitionInIfPossible = withTransitionIn;
    }
}

/*
 * Wrapper class for an animationClip or mainMixer in the Playables Graph of InteractiveFacialAnimation
 * Manages access to PlayablesExtensions functions
 * For Mixers, an automatic loop is implemented. update() needs to be called each frame for it to work properly.
 * 
 */
public class IFAClip
{
    // Data Structures, in order to properly interface clips in the playables Graph
    Dictionary<string, int> playablesDict;
    AnimationMixerPlayable mixerEmotionPlayable;

    // Constants
    public string playablesEmotionKey;              // currentlyPlaying + emotionNumber;
    bool isMixer;                                   // Is this clip a mainMixer or a transitionIn?
    readonly float mainLoopTriggerTime = 1.0f;
    readonly int mainMixerMainIndex = 0;
    readonly int mainMixerCopyIndex = 1;

    // Persistent Variables for Update
    bool fInitLoopMain = true;
    bool fPlayLoopMain = false;
    float mainLoopCurrentTime = 0f;

    // Constructor
    public IFAClip(Dictionary<string, int> playablesDict, AnimationMixerPlayable mixerEmotionPlayable, string playablesEmotionKey, bool isMixer)
    {
        this.playablesDict = playablesDict;
        this.mixerEmotionPlayable = mixerEmotionPlayable;
        this.playablesEmotionKey = playablesEmotionKey;
        this.isMixer = isMixer;  
    }

    public IFAClip(Dictionary<string, int> playablesDict, AnimationMixerPlayable mixerEmotionPlayable, string playablesEmotionKey, bool isMixer, 
        float mainLoopTriggerTime, int mainMixerMainIndex, int mainMixerCopyIndex) 
        : this(playablesDict, mixerEmotionPlayable, playablesEmotionKey, isMixer)
    {
        this.mainLoopTriggerTime = mainLoopTriggerTime;
        this.mainMixerMainIndex = mainMixerMainIndex;
        this.mainMixerCopyIndex = mainMixerCopyIndex;
    }

    // Needs to be called each frame
    public void update()
    {
        if(isMixer)
        {
            // If close to the end of a main clip, set loop starting flag
            if (((mixerEmotionPlayable.GetInput(playablesDict[playablesEmotionKey]).GetInput(mainMixerMainIndex).GetDuration() - 
                  mixerEmotionPlayable.GetInput(playablesDict[playablesEmotionKey]).GetInput(mainMixerMainIndex).GetTime()) <
                  mainLoopTriggerTime))
            {
                fPlayLoopMain = true;
            }

            // Ensure a smooth loop to the beginning for the main animation. Based on linearly interpolating the running clip with a copy of itself.
            if (fPlayLoopMain)
            {
                playLoopMainToStart();
            }
        }
    }

    public void setWeight(float newWeight)
    {
        mixerEmotionPlayable.SetInputWeight(playablesDict[playablesEmotionKey], newWeight);
    }

    public float getWeight()
    {
        return mixerEmotionPlayable.GetInputWeight(playablesDict[playablesEmotionKey]);
    }

    public void reset()
    {
        if(isMixer)
        {
            if (fPlayLoopMain)
            {
                interruptLoop();
            }
            mixerEmotionPlayable.GetInput(playablesDict[playablesEmotionKey]).GetInput(mainMixerMainIndex).SetTime(0f);
            mixerEmotionPlayable.GetInput(playablesDict[playablesEmotionKey]).GetInput(mainMixerMainIndex).SetDone(false);
        }
        else
        {
            mixerEmotionPlayable.GetInput(playablesDict[playablesEmotionKey]).SetTime(0f);
            mixerEmotionPlayable.GetInput(playablesDict[playablesEmotionKey]).SetDone(false);
        }
    }

    public double getDuration()
    {
        if(isMixer)
            return mixerEmotionPlayable.GetInput(playablesDict[playablesEmotionKey]).GetInput(mainMixerMainIndex).GetDuration(); 
        else
            return mixerEmotionPlayable.GetInput(playablesDict[playablesEmotionKey]).GetDuration();
    }

    public double getTime()
    {
        if(isMixer)
            return mixerEmotionPlayable.GetInput(playablesDict[playablesEmotionKey]).GetInput(mainMixerMainIndex).GetTime();
        else
            return mixerEmotionPlayable.GetInput(playablesDict[playablesEmotionKey]).GetTime();
    }

    // Loops the clip by lerping between the main and the copy clip
    private void playLoopMainToStart()
    {
        // Init
        if (fInitLoopMain)
        {
            mixerEmotionPlayable.GetInput(playablesDict[playablesEmotionKey]).GetInput(mainMixerCopyIndex).SetTime(0f);
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
        upcomingBlendWeight = IFALerp.linear(pStart, pEnd, mu);

        // Set Weights
        mixerEmotionPlayable.GetInput(playablesDict[playablesEmotionKey]).SetInputWeight(mainMixerMainIndex, 1f - upcomingBlendWeight);
        mixerEmotionPlayable.GetInput(playablesDict[playablesEmotionKey]).SetInputWeight(mainMixerCopyIndex, upcomingBlendWeight);

        // CleanUp
        if (upcomingBlendWeight >= mainLoopTriggerTime)
        {
            float timeElapsed = (float)mixerEmotionPlayable.GetInput(playablesDict[playablesEmotionKey]).GetInput(mainMixerCopyIndex).GetTime();
            mixerEmotionPlayable.GetInput(playablesDict[playablesEmotionKey]).GetInput(mainMixerMainIndex).SetTime(timeElapsed);
            interruptLoop();
        }
    }

    // If playLoopMainToStart() has to be interrupted, this function resets the mainMixer and associated parameters
    private void interruptLoop()
    {
        mixerEmotionPlayable.GetInput(playablesDict[playablesEmotionKey]).SetInputWeight(mainMixerMainIndex, 1.0f);
        mixerEmotionPlayable.GetInput(playablesDict[playablesEmotionKey]).SetInputWeight(mainMixerCopyIndex, 0.0f);

        mainLoopCurrentTime = 0.0f;
        fInitLoopMain = true;
        fPlayLoopMain = false;
    }
}

// Does an interface make any more sense than inheritance in this case?
// 
interface IFABlender
{
    void addActiveClips(List<IFAClip> activeClips);
    bool isInterruptable();
    bool isBlendingDone();
    void update();
}

/*
 * A Blender for blending between main clips (Without transition animations)
 * To blend, update needs to be called each frame
 */
public class IFABlenderMain : IFABlender
{
    // Data structures
    List<IFAClip> oldMainClips;         // The clips that are blended from
    float[] oldMainClipsInitialWeight;  // The initial weight of the clips
    IFAClip newMainClip;                // The clip that is blended to
    List<IFAClip> allClips;             // All clips for easier access

    // Persistant Variables for Update
    bool fInitTransition = true;
    float currentTime = 0f;

    // Constants
    readonly string interpolationMode;
    readonly float blendDuration = 0.5f;
    readonly float pBezierP1 = 0.7f;
    readonly float pBezierP2 = 0.3f;

    // State-Representing Variables
    bool blendingDone = false;

    // For drawing
    DrawGraphOnImage drawGraphOnImage;

    // Constructor
    public IFABlenderMain(List<IFAClip> oldMain, IFAClip newMain, string interpolationMode, DrawGraphOnImage drawGraphOnImage)
    {
        this.oldMainClips = oldMain;
        this.newMainClip = newMain;
        this.interpolationMode = interpolationMode;
        this.drawGraphOnImage = drawGraphOnImage;
        createAllClips();
        createOldMainClipsInitialWeights();
    }
    public IFABlenderMain(List<IFAClip> oldMain, IFAClip newMain, string interpolationMode, DrawGraphOnImage drawGraphOnImage,
        float blendDuration, float bezierP1, float bezierP2)
        : this(oldMain, newMain, interpolationMode, drawGraphOnImage)
    {
        this.blendDuration = blendDuration;
        this.pBezierP1 = bezierP1;
        this.pBezierP2 = bezierP2;
    }

    // Constructor Extension
    private void createAllClips()
    {
        allClips = new List<IFAClip>();

        if(oldMainClips != null)
        {
            foreach (IFAClip clip in oldMainClips)
            {
                allClips.Add(clip);
            }
        }

        allClips.Add(newMainClip);
    }
    // Constructor Extension
    private void createOldMainClipsInitialWeights()
    {
        oldMainClipsInitialWeight = new float[oldMainClips.Count];
        for (int i = 0; i < oldMainClips.Count; i++)
        {
            oldMainClipsInitialWeight[i] = oldMainClips[i].getWeight();
        }
    }

    // Needs to be called each frame
    public void update()
    {
        if(!blendingDone)
        {
            playTransition();
        }
    }

    // MODIFIES ACTIVECLIPS
    // Appends clips that currently have a weight > 0. Will not append a clip if is already in the list
    public void addActiveClips(List<IFAClip> activeClips)
    {
        if (!blendingDone)
        {
            foreach (IFAClip clip in allClips)
            {
                if (!isClipAlreadyinClips(clip, activeClips))
                    activeClips.Add(clip);
            }
        }
        if (blendingDone)
        {
            if (!isClipAlreadyinClips(newMainClip, activeClips))
                activeClips.Add(newMainClip);
        }
    }

    // addActiveClips Extension
    private bool isClipAlreadyinClips(IFAClip clip, List<IFAClip> clipList)
    {
        foreach (IFAClip clipInList in clipList)
        {
            if (clipInList.playablesEmotionKey.Equals(clip.playablesEmotionKey))
                return true;
        }
        return false;
    }

    public bool isInterruptable()
    {
        return currentTime >= blendDuration;
    }

    public bool isBlendingDone()
    {
        return blendingDone;
    }

    // Function that handles the actual blending. Called each frame in update
    private void playTransition()
    {
        // Initialize Transition when it begins
        if (fInitTransition)
        {
            newMainClip.reset();  // TODO: Might need to comment this back in if auto-main-loop doesnt work

            fInitTransition = false;
        }

        // LERP Transition and currently playing emotion
        // Interpolation website: http://paulbourke.net/miscellaneous/interpolation/
        currentTime += Time.deltaTime;
        float mu = currentTime / blendDuration;
        float t;
        float upcomingBlendWeight = 0;
        float pStart = 0f;
        float pEnd = 1f;

        switch (interpolationMode)
        {
            case "Linear":
                upcomingBlendWeight = IFALerp.linear(pStart, pEnd, mu);
                break;
            case "Cubic":
                upcomingBlendWeight = IFALerp.cubic(pStart, pEnd, mu);
                break;
            case "Bezier":
                upcomingBlendWeight = IFALerp.bezier(pStart, pEnd, mu, pBezierP1, pBezierP2);
                break;
            default:
                break;
        }

        // Draw for display purposes
        if(drawGraphOnImage)
        {
            drawGraphOnImage.drawPoint(0, pStart, Color.red);
            drawGraphOnImage.drawPoint(1, pEnd, Color.green);
            drawGraphOnImage.drawPoint(mu, upcomingBlendWeight, Color.black);
        }



        // Set Blend Weights
        if(oldMainClips != null)
        {
            for (int i = 0; i < oldMainClips.Count; i++)
            {
                oldMainClips[i].setWeight((1f - upcomingBlendWeight) * oldMainClipsInitialWeight[i]);
            }
        }
        newMainClip.setWeight(upcomingBlendWeight);

        // TODO: Update Clips?
        foreach(IFAClip clip in allClips)
        {
            clip.update();
        }


        // End Transition CleanUp, Prepare playing Main
        if (currentTime >= blendDuration)
        {
            blendingDone = true;
        }
    }

}

/*
 * A Blender for blending between main clips with transitionIn animations
 * Will blend between oldMain and transitionClip. Then, the transitionClip will fully play. After, the newMainClip will play.
 * To blend, update needs to be called each frame
 */
public class IFABlenderTransition : IFABlender
{
    // Data structures
    List<IFAClip> oldMainClips;         // The clips that are blended from
    float[] oldMainClipsInitialWeight;  // The initial weight of the clips
    IFAClip transitionClip;             // The clip that is blended to
    IFAClip newMainClip;                // The clip that is played after the transitionClip is done playing
    List<IFAClip> allClips;             // All clips for easier access

    // Persistant Variables for Update
    bool fInitTransition = true;
    float currentTime = 0f;
    bool fPlayMainAfterTransition = false;
    bool transitionDone = false;

    // Constants
    readonly string interpolationMode;
    readonly float blendDuration = 0.5f;
    readonly float pBezierP1 = 0.7f;
    readonly float pBezierP2 = 0.3f;

    // State-Representing Variables
    bool blendingDone = false;

    // For drawing
    DrawGraphOnImage drawGraphOnImage;

    // Constructor
    public IFABlenderTransition(List<IFAClip> oldMain, IFAClip transition, IFAClip newMain, string interpolationMode, DrawGraphOnImage drawGraphOnImage)
    {
        this.oldMainClips = oldMain;
        this.transitionClip = transition;
        this.newMainClip = newMain;
        this.interpolationMode = interpolationMode;
        this.drawGraphOnImage = drawGraphOnImage;
        createAllClips();
        createOldMainClipsInitialWeights();
    }
    public IFABlenderTransition(List<IFAClip> oldMain, IFAClip transition, IFAClip newMain, string interpolationMode, DrawGraphOnImage drawGraphOnImage,
    float blendDuration, float bezierP1, float bezierP2)
    : this(oldMain, transition, newMain, interpolationMode, drawGraphOnImage)
    {
        this.blendDuration = blendDuration;
        this.pBezierP1 = bezierP1;
        this.pBezierP2 = bezierP2;
    }

    // Constructor Extension
    private void createAllClips()
    {
        allClips = new List<IFAClip>();
        if (oldMainClips != null)
        {
            foreach (IFAClip clip in oldMainClips)
            {
                allClips.Add(clip);
            }
        }
        allClips.Add(transitionClip);
        allClips.Add(newMainClip);
    }

    // Constructor Extension
    private void createOldMainClipsInitialWeights()
    {
        oldMainClipsInitialWeight = new float[oldMainClips.Count];
        for(int i = 0; i < oldMainClips.Count; i++)
        {
            oldMainClipsInitialWeight[i] = oldMainClips[i].getWeight();
        }
    }

    // Needs to be called each frame
    public void update()
    {
        if (!transitionDone)
        {
            playTransition();
        }
        if (transitionDone && !blendingDone)
        {
            playMain();
        }
    }

    // MODIFIES ACTIVECLIPS
    // Appends clips that currently have a weight > 0
    public void addActiveClips(List<IFAClip> activeClips)
    {
        if (!blendingDone)
        {
            foreach (IFAClip clip in allClips)
            {
                if(!isClipAlreadyinClips(clip, activeClips))
                    activeClips.Add(clip);
            }
        }
        if (blendingDone)
        {
            if (!isClipAlreadyinClips(newMainClip, activeClips))
                activeClips.Add(newMainClip);
        }
    }

    // addActiveClips Extension
    private bool isClipAlreadyinClips(IFAClip clip, List<IFAClip> clipList)
    {
        foreach(IFAClip clipInList in clipList)
        {
            if (clipInList.playablesEmotionKey.Equals(clip.playablesEmotionKey))
                return true;
        }
        return false;    }

    public bool isInterruptable()
    {
        return currentTime >= blendDuration;
    }

    public bool isBlendingDone()
    {
        return blendingDone;
    }

    // Function that handles the actual blending. Called each frame in update
    private void playTransition()
    {
        
        // Initialize Transition when it begins
        if (fInitTransition)
        {
            transitionClip.reset();

            fInitTransition = false;
            fPlayMainAfterTransition = false;   // In case a new transition starts while a transitionIn animation is still playing
        }
        
        // LERP Transition and currently playing emotion
        // Interpolation website: http://paulbourke.net/miscellaneous/interpolation/
        currentTime += Time.deltaTime;
        float mu = currentTime / blendDuration;
        float t;
        float upcomingBlendWeight = 0;
        float pStart = 0f;
        float pEnd = 1f;

        switch (interpolationMode)
        {
            case "Linear":
                upcomingBlendWeight = IFALerp.linear(pStart, pEnd, mu);
                break;
            case "Cubic":
                upcomingBlendWeight = IFALerp.cubic(pStart, pEnd, mu);
                break;
            case "Bezier":
                upcomingBlendWeight = IFALerp.bezier(pStart, pEnd, mu, pBezierP1, pBezierP2);
                break;
            default:
                break;
        }

        // Draw for display purposes
        if (drawGraphOnImage)
        {
            drawGraphOnImage.drawPoint(0, pStart, Color.red);
            drawGraphOnImage.drawPoint(1, pEnd, Color.green);
            drawGraphOnImage.drawPoint(mu, upcomingBlendWeight, Color.black);
        }


        // Set Blend Weights
        if (oldMainClips != null)
        {
            for (int i = 0; i < oldMainClips.Count; i++)
            {
                oldMainClips[i].setWeight((1f - upcomingBlendWeight) * oldMainClipsInitialWeight[i]);
            }
        }
        transitionClip.setWeight(upcomingBlendWeight);

        // Update Clips?
        foreach (IFAClip clip in allClips)
        {
            clip.update();
        }


        // End Transition CleanUp, Prepare playing Main
        if (currentTime >= blendDuration)
        {
            fPlayMainAfterTransition = true;
            fInitTransition = true;
            currentTime = 0;
            //previousEmotionNumber = emotionNumber;
            transitionDone = true;
        }
        //normalize = true;
        
    }

    private void playMain()
    {
        if ((transitionClip.getDuration() - transitionClip.getTime()) < 0.05f)
        {
            //currentlyPlaying = transitionEmotion;
            fPlayMainAfterTransition = false;
            transitionClip.setWeight(0.0f);
            newMainClip.reset();
            newMainClip.setWeight(1.0f);
            //normalize = true;
            blendingDone = true;
        }
    }

}

/*
 * Offers Lerping functions
 */
public static class IFALerp
{
    public static float linear(float startPoint, float endPoint, float interpolation)
    {
        return Mathf.Lerp(startPoint, endPoint, interpolation);
    }

    public static float cubic(float startPoint, float endPoint, float interpolation)
    {
        float t = (1 - Mathf.Cos(interpolation * Mathf.PI)) / 2;
        return Mathf.Lerp(startPoint, endPoint, t);
    }

    public static float bezier(float startPoint, float endPoint, float interpolation, float bezierP1 = 0.3f, float bezierP2 = 0.7f)
    {
        // Bezier, B-Spline? 4-point interpolation, so they need two manually defined points. No recommendations?
        // S. 148, kurze Erwänung unter Nonlinear Interpolation: https://books.google.de/books?id=yEzrBgAAQBAJ&pg=PA147&lpg=PA147&dq=facial+animation+transition+interpolation&source=bl&ots=ntfbn7k6ww&sig=FDfcPAnj_mr76FcedSKEx4zrjRU&hl=de&sa=X&ved=2ahUKEwi_u42K3q7fAhUHmYsKHfYJDncQ6AEwA3oECAcQAQ#v=onepage&q=facial%20animation%20transition%20interpolation&f=false
        // Linear, Cubic B-Spline, Cardinal Spline: https://www.researchgate.net/publication/44250675_Parametric_Facial_Expression_Synthesis_and_Animation
        // Best Bezier explanation ever: https://denisrizov.com/2016/06/02/bezier-curves-unity-package-included/
        float t = interpolation;

        float u = 1f - t;
        float t2 = t * t;
        float u2 = u * u;
        float u3 = u2 * u;
        float t3 = t2 * t;

        return
            (u3) * startPoint +
            (3f * u2 * t) * bezierP1 +
            (3f * u * t2) * bezierP2 +
            (t3) * endPoint;
    }
}


/*
 * As PlayableExtension methods cannot be overwritten, this class is sadly useless
public class IFAMainMixerBehaviour : PlayableBehaviour
{
    bool fPlayLoopMain = false;
    bool fInitLoopMain = false;
    float currentTime = 0f;
    int mainIndex = 0;
    int copyIndex = 1;

    public AnimationMixerPlayable mixerPlayable;

    float loopTriggerTime;

    public void constructor(AnimationMixerPlayable mixerPlayable, float loopTriggerTime)
    {
        this.mixerPlayable = mixerPlayable;
        this.loopTriggerTime = loopTriggerTime;
    }
    public void constructor2(AnimationMixerPlayable mixerPlayable, float loopTriggerTime, int mainIndex, int copyIndex) 
    {
        constructor(mixerPlayable, loopTriggerTime);
        this.mainIndex = mainIndex;
        this.copyIndex = copyIndex;
    }


    public override void PrepareFrame(Playable playable, FrameData info)
    {
        // If close to the end of a main clip, set loop starting flag
        if (((mixerPlayable.GetInput(mainIndex).GetDuration() - mixerPlayable.GetInput(mainIndex).GetTime()) < loopTriggerTime))
        {
            fPlayLoopMain = true;
        }

        // Ensure a smooth loop to the beginning for the main animation. Based on linearly interpolating the running clip with a copy of itself.
        if (fPlayLoopMain)
        {
            playLoopMainToStart();
        }

        base.PrepareFrame(playable, info);
    }

    public override void PlayableExtensions.SetTime(Playable a, double value)
    {

    }

    public void SetTime(double value)
    {
        if (fPlayLoopMain)
            interruptLoop();

        mixerPlayable.GetInput(mainIndex).SetTime(value);
        Debug.Log("IFAMainMixerBehaviour.SetTime called");
        
    }

    public void SetDone(bool value)
    {
        mixerPlayable.GetInput(mainIndex).SetDone(value);
        Debug.Log("IFAMainMixerBehaviour.SetDone called");
    }

    public double GetTime()
    {
        Debug.Log("IFAMainMixerBehaviour.GetTime called");
        return mixerPlayable.GetInput(mainIndex).GetTime();
    }

    public double GetDuration()
    {
        Debug.Log("IFAMainMixerBehaviour.GetDuration called");
        return mixerPlayable.GetInput(mainIndex).GetDuration();
    }

    public void printLine()
    {
        Debug.Log("A PRINTED LINE");
    }

    private void playLoopMainToStart()
    {
        // Init
        if (fInitLoopMain)
        {
            mixerPlayable.GetInput(copyIndex).SetTime(0f);
            fInitLoopMain = false;
        }

        // LERP Init
        currentTime += Time.deltaTime;
        float mu = currentTime / loopTriggerTime;
        float t;
        float upcomingBlendWeight = 0;
        float pStart = 0f;
        float pEnd = 1f;

        // Linear
        upcomingBlendWeight = IFALerp.linear(pStart, pEnd, mu);

        // Set Weights
        mixerPlayable.SetInputWeight(mainIndex, 1f - upcomingBlendWeight);
        mixerPlayable.SetInputWeight(copyIndex, upcomingBlendWeight);


        // CleanUp
        if (upcomingBlendWeight >= loopTriggerTime)
        {
            float timeElapsed = (float)mixerPlayable.GetInput(copyIndex).GetTime();
            mixerPlayable.GetInput(mainIndex).SetTime(timeElapsed);
            interruptLoop();
        }
    }

    private void interruptLoop()
    {
        mixerPlayable.SetInputWeight(mainIndex, 1.0f);
        mixerPlayable.SetInputWeight(copyIndex, 0.0f);

        currentTime = 0.0f;
        fInitLoopMain = true;
        fPlayLoopMain = false;
    }
}
*/