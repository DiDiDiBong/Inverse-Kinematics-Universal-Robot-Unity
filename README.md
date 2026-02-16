Citation

Damien Mazeas. (2023). mazeasdamien/Inverse-Kinematics-Universal-Robot-Unity: UnityUniversalRobots (UnityUniversalRobots). Zenodo. https://doi.org/10.5281/zenodo.15265718

---

# Inverse Kinematics for UR16e Robot

This repository contains a C# implementation of the Inverse Kinematics algorithm for the UR16e robot. The code calculates the joint angles required to achieve a desired end-effector pose using the robot's Denavit-Hartenberg (DH) parameters.

![image](https://user-images.githubusercontent.com/58029218/231768653-8d372e29-0603-4279-a48a-9854aff4a4c9.png)

### Usage (main branch)

On the `main` branch, control is done directly in the Unity editor:

- In **Editor** or **Play** mode, move the **IK** GameObject to change the end-effector pose.
- The script computes the corresponding joint angles for the UR16e.

![image](https://user-images.githubusercontent.com/58029218/231768581-8fc544e5-1a10-46d8-9c5c-56f96ce347c0.png)

---

## UI Branch

There is a branch that adds a user interface for teaching and replaying motions of the UR16e:

- Branch name: `henry-with-ui`
- Contributor: [@henry2craftman](https://github.com/henry2craftman)
- Additional scripts:
  - `UIController.cs` – UI controls for end-effector position (X, Y, Z) and rotation, plus “teaching” functionality to record robot poses and gripper states.
  - `AutomationController.cs` – Executes recorded sequences so the robot can replay taught motions.
- Teaching data is saved persistently (e.g. `teachingData.txt`).
- Additional scenes (configured for HDRP):
  - `main_animation` – Demonstrates repetitive robot movements.
  - `main_ui` – Provides the UI for creating and managing teaching data.
