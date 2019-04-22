# Interactively Controllable Facial Animation System Prototype

The Unity project used for developing and testing the prototype for my masters thesis.

## Getting Started

Clone or download the project, then open it with Unity version 2018.3. Downloading may take a while.

## Contents 

### Script Components

The implementation of the different system mentioned in the thesis are organized into different script components. They can be found under Assets/MA-Files.
Detailed setup of each individual component is detailed in the inspector mouseover-text.

The script components necessary for facial animation are:

```
1. InteractiveFacialAnimationV2.cs
2. InteractiveFacialAnimationV2Controller.cs
```

Each character partaking in the facial animation system requires the InteractiveFacialAnimationV2 component.

The InteractiveFacialAnimationV2Controller component is only needed once per scene (or once per facial animation "group"). 
It registers the individual InteractiveFacialAnimationV2 components and allows for global setting and deleting of emotions.
(Arguably the controller is not necessary and should be a part of the individual components, leaving the controller implementation up to the needs of the potential user.
The reason it is still in use is that it is a remnant of earlier architectures.)

The script components necessary for speech animation are:

```
1. LipSync.cs
2. energyBasedLipSync.cs
```

Both components need to be attached to each character. Furthermore an AudioSource component is required. The phonemeBasedLipSync component is only for demonstration purposes and will not work with arbitrary audio.

The script components necessary for lookat animation are:

```
1. LookAt.cs
```

Each character requires a LookAt component.

### Demo Scenes

The project includes various scenes demonstrating the prototype.

**ExpressionMixingDemo** showcases the results of interpolating two expressions. Play the scene and click on one of the buttons. The left model will display the first expression, the middle model will display the second expression and the right model will display a linearly weighted interpolation with a coefficient of 0.5.

**LipSyncDemo** showcases different realtime speech animation approaches. Simply play the scene to compare them.

**TransitionDemo** showcases how using interpolation for transitioning between emotional expressions affects realism for static expressions. The left model will display linear interpolation, the middle model will display cubic interpolation and the right model will display one possible bezier interpolation. Enter a valid expression in the text field and press enter to induce a transition. Valid expressions can be found in the InteractiveFacialAnimationV2 component of each character.

**MAScene** is the initial prototyping scene. The active model in this scene has all component attached and can be used to for example test out the lookAt component.

