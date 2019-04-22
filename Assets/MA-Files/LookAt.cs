﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LookAt : MonoBehaviour
{

    // Public Variables
    [Header("Fill In Components and Pre-Runtime Settings")]
    [Tooltip("Head joint of the character")]
    public GameObject head;
    [Tooltip("Left eye joint of the character")]
    public GameObject leftEye;
    [Tooltip("Right eye joint of the character")]
    public GameObject rightEye;
    [Tooltip("The Gameobject which will be rotated towards.")]
    public GameObject lookAtTarget;
    [Tooltip("Optional: The left eye of the character that is being looked at (for example the left eye camera in VR). Required for facemode.")]
    public GameObject lookAtLeftEye;
    [Tooltip("Optional: The right eye of the character that is being looked at (for example the right eye camera in VR). Required for facemode.")]
    public GameObject lookAtRightEye;
    [Tooltip("Optional: The mouth of the character that is being looked at. Required for facemode.")]
    public GameObject lookAtMouth;
    [Tooltip("Fixes the initial head joint rotation of the character 'Claire'")]
    public bool HACKFixInitialHeadRotiationWhenUsingInteractiveFacialAnimation;

    [Header("Controls")]
    [Tooltip("Whether gaze or pursuit behaviour should be exhibited.")]
    public bool pursuit;
    [Tooltip("Whether the character should return to a neutral position or not if the target position cannot be reached.")]
    public bool tryFollowTarget;
    [Tooltip("Whether to apply eye or head movement from a AnimationController")]
    public bool stopApplyingAnimation;
    [Tooltip("Whether the head should be able to move or not.")]
    public bool stopHeadMovement;
    [Tooltip("Whether the character should look at the object specified by lookAtObject or the three other ones.")]
    public bool faceMode;

    [Header("Fine-Tuning Parameters")]
    [Tooltip("Maximum angle head and eyes will rotate before stopping")]
    public float MaxTurnAngle = 50.0f;
    [Tooltip("Pursuit Mode: Difference between head and eyes after which head will start moving.")]
    public float pursuitStartHeadFollow = 13.0f;
    [Tooltip("Pursuit Mode: Difference between head and eyes after which head will stop moving.")]
    public float pursuitStopHeadFollow = 12.0f;
    [Tooltip("Pursuit Mode: Maximum speed of the head. Should not exceed eye speed.")]
    public float pursuitHeadSpeed = 3.0f;
    [Tooltip("Pursuit Mode: Maximum speed of the eyes.")]
    public float pursuitEyeSpeed = 10.0f;
    [Tooltip("Gaze Mode: Difference between head and eyes after which head will start moving.")]
    public float gazeStartHeadFollow = 14.5f;
    [Tooltip("Gaze Mode: Difference between head and eyes after which head will stop moving.")]
    public float gazeStopHeadFollow = 14.0f;
    [Tooltip("Gaze Mode: Maximum speed of the head. Should not exceed eye speed.")]
    public float gazeHeadSpeed = 3.0f;
    [Tooltip("Gaze Mode: Maximum speed of the eyes.")]
    public float gazeEyeSpeed = 15.0f;

    // The actual object that will be in focus
    private GameObject lookAtObject;

    // have to store last rotation to undo animation, otherwise slerp doesn't work
    private Quaternion lastHeadRotation;
    private Quaternion lastLeftEyeRotation;
    private Quaternion lastRightEyeRotation;

    // Match initial rotation to the object this script is attached to
    private Quaternion headOffsetRotation;
    private Quaternion leftEyeOffsetRotation;
    private Quaternion rightEyeOffsetRotation;

    // Local rotation stores to keep animation (instead of being overwritten by lookAt)
    private Quaternion headAnimationClipOffset;
    private Quaternion headInitRotation;
    private Quaternion leftEyeAnimationClipOffset;
    private Quaternion leftEyeInitRotation;
    private Quaternion rightEyeAnimationClipOffset;
    private Quaternion rightEyeInitRotation;

    //float xVelocity = 0.0f;
    //float yVelocity = 0.0f;
    //float zVelocity = 0.0f;

    // Various triggers for head Follow motion
    private bool headFollow = true;
    private float leftEyeLookAtDiffAngle;
    private float rightEyeLookAtDiffAngle;
    private GameObject gazePosition;
    bool returnToNeutralState = false;          // Base whether the head should return to neutral on if the eyes return to neutral
    
    private float faceSaccadeTimer = 0f;

    // Pursuit/Gaze mode variables
    private float eyeSpeed;
    private float headSpeed;
    private float startHeadFollow;
    private float stopHeadFollow;


    // Test Process LookFor Class
    private ProcessLookFor headProcessLookFor;
    private ProcessLookFor leftEyeProcessLookFor;
    private ProcessLookFor rightEyeProcessLookFor;


    // Use this for initialization
    void Start()
    {
        lookAtObject = lookAtTarget;

        // Save initial rotation to later calculate deviations from it
        headInitRotation = head.transform.localRotation;
        leftEyeInitRotation = leftEye.transform.localRotation;
        rightEyeInitRotation = rightEye.transform.localRotation;

        if (HACKFixInitialHeadRotiationWhenUsingInteractiveFacialAnimation)
            headInitRotation = new Quaternion(-0.15f, 0, 0, 1.0f); // SOMEHOW INITIAL ROTATION IS WRONG; SOMEHOW THIS IS RIGHT. THIS IS A HACK

        // find rotation needed to get the object's z facing forward and y facing upwards
        headOffsetRotation = Quaternion.Inverse(this.transform.rotation) * head.transform.rotation;
        leftEyeOffsetRotation = Quaternion.Inverse(this.transform.rotation) * leftEye.transform.rotation;
        rightEyeOffsetRotation = Quaternion.Inverse(this.transform.rotation) * rightEye.transform.rotation;

        //headOffsetLocalRotation = Quaternion.Inverse(this.transform.rotation) * head.transform.localRotation;

        headProcessLookFor = new ProcessLookFor(head, headOffsetRotation, this.gameObject);
        leftEyeProcessLookFor = new ProcessLookFor(leftEye, leftEyeOffsetRotation, this.gameObject);
        rightEyeProcessLookFor = new ProcessLookFor(rightEye, rightEyeOffsetRotation, this.gameObject);

        gazePosition = new GameObject();
    }

    void Update()
    {
        // Reset the animation offset, so a new one can be applied and to not interfere with the ProcessLookFor calculations
        // Relies on the animation data having been applied at this point!
        if (!stopApplyingAnimation)
        {
            head.transform.localRotation = Quaternion.Inverse(headAnimationClipOffset) * head.transform.localRotation;
            leftEye.transform.localRotation = Quaternion.Inverse(leftEyeAnimationClipOffset) * leftEye.transform.localRotation;
            rightEye.transform.localRotation = Quaternion.Inverse(rightEyeAnimationClipOffset) * rightEye.transform.localRotation;
        }

        lastHeadRotation = head.transform.rotation;
        lastLeftEyeRotation = leftEye.transform.rotation;
        lastRightEyeRotation = rightEye.transform.rotation;
    }


    void LateUpdate()
    {
        // Find difference from neutral rotation (looking straight forward) to the animation rotation (looking whereever)
        if (!stopApplyingAnimation)
        {
            headAnimationClipOffset = head.transform.localRotation * Quaternion.Inverse(headInitRotation);
            leftEyeAnimationClipOffset = leftEye.transform.localRotation * Quaternion.Inverse(leftEyeInitRotation);
            rightEyeAnimationClipOffset = rightEye.transform.localRotation * Quaternion.Inverse(rightEyeInitRotation);
        }

        // Set constants based on mode
        if (pursuit)
        {
            eyeSpeed = pursuitEyeSpeed;
            headSpeed = pursuitHeadSpeed;
            startHeadFollow = pursuitStartHeadFollow;
            stopHeadFollow = pursuitStopHeadFollow;
        }
        else
        {
            eyeSpeed = gazeEyeSpeed;
            headSpeed = gazeHeadSpeed;
            startHeadFollow = gazeStartHeadFollow;
            stopHeadFollow = gazeStopHeadFollow;
        }

        // Change Object that is looked at depending on mode
        if(faceMode)
        {
            // Select object passed on probability. Only reflects real behaviour very roughly.
            faceSaccadeTimer -= Time.deltaTime;
            if(faceSaccadeTimer <= 0.0f)
            {
                // Eye or Mouth
                if (Random.value < 0.3f)
                {
                    lookAtObject = lookAtMouth;
                }
                else
                {
                    // Left or Right Eye (Left Eye is preferred)
                    if (Random.value < 0.3f)
                        lookAtObject = lookAtRightEye;
                    else
                        lookAtObject = lookAtLeftEye;
                }
                faceSaccadeTimer = Mathf.Lerp(1.0f, 2.0f, Random.value);
            }
        }
        else    // Not faceMode
        {
            lookAtObject = lookAtTarget;
        }

        // Process lookAts in order: first head, then eyes
        // Process Head
        if (!stopHeadMovement)
        {
            // Based on last frames calculations, let the head should follow lookAtObject or not
            if (headFollow)
            {
                //ProcessLookFor(head, headOffsetRotation, lastHeadRotation, headSpeed, lookAtObject.transform);
                headProcessLookFor.process(lastHeadRotation, lookAtObject.transform);
                if (returnToNeutralState)
                    headProcessLookFor.returnToNeutral(lastHeadRotation, headSpeed);
                else
                    headProcessLookFor.applyProcess(lastHeadRotation, headSpeed);

                gazePosition.transform.position = new Vector3(lookAtObject.transform.position.x, lookAtObject.transform.position.y, lookAtObject.transform.position.z);
            }
            else
            {
                //ProcessLookFor(head, headOffsetRotation, lastHeadRotation, headSpeed, gazePosition.transform);
                headProcessLookFor.process(lastHeadRotation, gazePosition.transform);
                if (returnToNeutralState)
                    headProcessLookFor.returnToNeutral(lastHeadRotation, headSpeed);
                else
                    headProcessLookFor.applyProcess(lastHeadRotation, headSpeed);
            }
            //ProcessLookFor(head, headOffsetRotation, lastHeadRotation, 2.0f, lookAtObject.transform);
        }

        // Process Eyes
        leftEyeProcessLookFor.process(lastLeftEyeRotation, lookAtObject.transform);
        rightEyeProcessLookFor.process(lastRightEyeRotation, lookAtObject.transform);
        bool leftEyeExceeds = leftEyeProcessLookFor.checkIfExceedsMaxTurnAngle(MaxTurnAngle);
        bool rightEyeExceeds = rightEyeProcessLookFor.checkIfExceedsMaxTurnAngle(MaxTurnAngle);

        if (tryFollowTarget)
        {
            if (!leftEyeExceeds && !rightEyeExceeds)
            {
                leftEyeProcessLookFor.applyProcess(lastLeftEyeRotation, eyeSpeed);
                rightEyeProcessLookFor.applyProcess(lastRightEyeRotation, eyeSpeed);
            }
        }
        else // Return to neutral Mode
        {
            if (leftEyeExceeds || rightEyeExceeds)                // then turn back
            {
                leftEyeProcessLookFor.returnToNeutral(lastLeftEyeRotation, eyeSpeed);
                rightEyeProcessLookFor.returnToNeutral(lastRightEyeRotation, eyeSpeed);
                returnToNeutralState = true;
            }
            else
            {
                leftEyeProcessLookFor.applyProcess(lastLeftEyeRotation, eyeSpeed);
                rightEyeProcessLookFor.applyProcess(lastRightEyeRotation, eyeSpeed);
                returnToNeutralState = false;
            }
        }

        //ProcessLookFor(leftEye, leftEyeOffsetRotation, lastLeftEyeRotation, eyeSpeed, lookAtObject.transform);
        //ProcessLookFor(rightEye, rightEyeOffsetRotation, lastRightEyeRotation, eyeSpeed, lookAtObject.transform);

        // Calculate by how much the current eye rotation deviates from neutral position
        leftEyeLookAtDiffAngle = Quaternion.Angle(leftEye.transform.localRotation, Quaternion.Inverse(leftEyeInitRotation));
        rightEyeLookAtDiffAngle = Quaternion.Angle(rightEye.transform.localRotation, Quaternion.Inverse(rightEyeInitRotation));
        //Debug.Log("Right Eye Angle: " + rightEyeLookAtDiffAngle);
        //Debug.Log("Left Eye Angle: " + leftEyeLookAtDiffAngle);

        // Decide whether the head should follow lookAtObject on the next frame
        if ((rightEyeLookAtDiffAngle + leftEyeLookAtDiffAngle) / 2.0f > startHeadFollow)
        {
            headFollow = true;
        }
        else if ((rightEyeLookAtDiffAngle + leftEyeLookAtDiffAngle) / 2.0f <= stopHeadFollow)
        {
            headFollow = false;
        }


        // Add difference from neutral rotation to animation rotation back in after the lookAt overwrite
        if (!stopApplyingAnimation)
        {
            head.transform.localRotation = headAnimationClipOffset * head.transform.localRotation;
            leftEye.transform.localRotation = leftEyeAnimationClipOffset * leftEye.transform.localRotation;
            rightEye.transform.localRotation = rightEyeAnimationClipOffset * rightEye.transform.localRotation;
        }

    }

    // process look for object
    void ProcessLookFor(GameObject inObject, Quaternion inOffsetRotation, Quaternion lastRotation, float inSpeed, Transform target)
    {
        // now look at player by rotating the true forward rotation by the look at rotation
        Vector3 toCamera = target.transform.position - inObject.transform.position;

        // look to camera.  this rotates forward vector towards camera
        // make sure to rotate by the object's offset first, since they aren't always forward
        Quaternion lookToCamera = Quaternion.LookRotation(toCamera);

        // find difference between forward vector and look to camera
        Quaternion diffQuat = Quaternion.Inverse(this.transform.rotation) * lookToCamera;

        // if outside range, lerp back to middle
        if (diffQuat.eulerAngles.y > MaxTurnAngle && diffQuat.eulerAngles.y < 360.0f - MaxTurnAngle)
            inObject.transform.rotation = Quaternion.Slerp(lastRotation, this.transform.rotation * inOffsetRotation, inSpeed * Time.deltaTime);
        else
        {
            // lerp rotation to camera, making sure to rotate by the object's offset since they aren't always forward
            inObject.transform.rotation = Quaternion.Slerp(lastRotation, lookToCamera * inOffsetRotation, inSpeed * Time.deltaTime);


            /** TRIED TO USE FORMULAS RELYING ON EULER ANGLES. DIDN'T WORK
                 
            Quaternion diffAngle = lastRotation * Quaternion.Inverse(lookToCamera * inOffsetRotation);
            //Debug.Log("TMepAngle: "+tempAngle);
            float XLeftMaxSpeedHoriz = 473 * (1 - Mathf.Exp(-diffAngle.eulerAngles.x / 7.8f));       // From "Realistic Avatar and Head Animation Using a Neurobiological Model of Visual Attention", Itti, Dhavale, Pighin
            float XLeftHorizDuration = 0.025f + 0.00235f * diffAngle.eulerAngles.x;                     // From "Eyes Alive", Lee, Badler
            float YLeftMaxSpeedHoriz = 473 * (1 - Mathf.Exp(-diffAngle.eulerAngles.y / 7.8f));      
            float YLeftHorizDuration = 0.025f + 0.00235f * diffAngle.eulerAngles.y;      
            float ZLeftMaxSpeedHoriz = 473 * (1 - Mathf.Exp(-diffAngle.eulerAngles.z / 7.8f));     
            float ZLeftHorizDuration = 0.025f + 0.00235f * diffAngle.eulerAngles.z;
            Debug.Log("Durations. X: " + XLeftMaxSpeedHoriz + " Y: " + YLeftMaxSpeedHoriz + " Z: " + ZLeftMaxSpeedHoriz);
            float xAngle = Mathf.SmoothDampAngle(lastRotation.eulerAngles.x, Quaternion.Inverse(lookToCamera * inOffsetRotation).eulerAngles.x, ref xVelocity, XLeftHorizDuration, XLeftMaxSpeedHoriz);
            float yAngle = Mathf.SmoothDampAngle(lastRotation.eulerAngles.y, Quaternion.Inverse(lookToCamera * inOffsetRotation).eulerAngles.y, ref yVelocity, YLeftHorizDuration, YLeftMaxSpeedHoriz);
            float zAngle = Mathf.SmoothDampAngle(lastRotation.eulerAngles.z, Quaternion.Inverse(lookToCamera * inOffsetRotation).eulerAngles.z, ref zVelocity, ZLeftHorizDuration, ZLeftMaxSpeedHoriz);
            inObject.transform.rotation = Quaternion.Euler(inObject.transform.rotation.eulerAngles.x, yAngle, inObject.transform.rotation.eulerAngles.z);

            //float resultOfSmoothDamp = Mathf.SmoothDampAngle(0, diffAngle.eulerAngles.y, ref zVelocity, leftHorizDuration, leftMaxSpeedHoriz);
            //Debug.Log("resultOfSmoothDamp :" + resultOfSmoothDamp);

            */
        }
    }

    public void setLookAtObject(GameObject lookAtObject)
    {
        headProcessLookFor.setLookAtObject(lookAtObject);
        leftEyeProcessLookFor.setLookAtObject(lookAtObject);
        rightEyeProcessLookFor.setLookAtObject(lookAtObject);
    }
}





public class ProcessLookFor
{
    // External
    private GameObject inObject;
    private GameObject rootObject;
    private Quaternion inOffsetRotation;

    // Internal
    private Vector3 toCamera;
    private Quaternion lookToCamera;
    private Quaternion diffQuat;

    float xVelocity = 0.0f;
    float yVelocity = 0.0f;
    float zVelocity = 0.0f;

    public ProcessLookFor(GameObject toBeRotatedObject, Quaternion initalOffsetRotation, GameObject rootParentObject)
    {
        inObject = toBeRotatedObject;
        rootObject = rootParentObject;
        inOffsetRotation = initalOffsetRotation;
    }

    public void process(Quaternion lastRotation, Transform target)
    {
        // now look at player by rotating the true forward rotation by the look at rotation
        toCamera = target.transform.position - inObject.transform.position;

        // look to camera.  this rotates forward vector towards camera
        // make sure to rotate by the object's offset first, since they aren't always forward
        lookToCamera = Quaternion.LookRotation(toCamera);

        // find difference between forward vector and look to camera
        diffQuat = Quaternion.Inverse(rootObject.transform.rotation) * lookToCamera;
    }

    public void applyProcess(Quaternion lastRotation, float inSpeed)
    {
        // lerp rotation to camera, making sure to rotate by the object's offset since they aren't always forward
        inObject.transform.rotation = Quaternion.Slerp(lastRotation, lookToCamera * inOffsetRotation, inSpeed * Time.deltaTime);

    }

    public bool checkIfExceedsMaxTurnAngle(float MaxTurnAngle)
    {
        // if outside range, lerp back to middle
        if (diffQuat.eulerAngles.y > MaxTurnAngle && diffQuat.eulerAngles.y < 360.0f - MaxTurnAngle)
            return true;
        else
            return false;
    }

    public void returnToNeutral(Quaternion lastRotation, float inSpeed)
    {
        inObject.transform.rotation = Quaternion.Slerp(lastRotation, rootObject.transform.rotation * inOffsetRotation, inSpeed * Time.deltaTime);
    }

    public void setLookAtObject(GameObject lookAtObject)
    {
        inObject = lookAtObject;
    }
}