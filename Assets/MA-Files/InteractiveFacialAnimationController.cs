using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/*
 * Hub class that is used to control facial animation on all characters.
 * To be attached to an emtpy GameObject.
 * 
 * You only ever need one of these in your scene. Multiple are possible, but unnecessary and possibly harder to manage.
 */
public class InteractiveFacialAnimationController : MonoBehaviour
{
    // Public Data Structures
    public List<InteractiveFacialAnimation> characterScripts;     // References to all PlayablePrototypesV2 in the scene
    [HideInInspector]
    public List<string> emotionNames = new List<string> { "Happy", "Sad", "Angry" };



    // Search and add all GameObjects with PerHeadControllers in the scene
    public void getAllHeadControllers()
    {
        InteractiveFacialAnimation[] characterScriptsArray = FindObjectsOfType<InteractiveFacialAnimation>();
        Debug.Log("Found " + characterScriptsArray.Length + " instances with this script attached");
        characterScripts = new List<InteractiveFacialAnimation>(characterScriptsArray);
    }

    // Update Emotions in all PerHeadControllers
    public void updateAllHeadControllers()
    {
        foreach (InteractiveFacialAnimation characterScript in characterScripts)
        {
            if (characterScript == null) characterScripts.Remove(characterScript);
            characterScript.updateEmotionList(emotionNames);
        }
    }


    public void transitionToEmotionAllHeadControllers(string emotion)
    {
        foreach (InteractiveFacialAnimation characterScript in characterScripts)
        {
            characterScript.playEmotion(emotion);
        }
    }

}
