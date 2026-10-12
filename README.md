Unity AI Rapid Prototyping Mini-Game

The goal of this project was not to build a fully polished commercial title, but rather **to experiment and test AI-driven workflows** for rapid prototyping game mechanics and logic inside Unity.

Experiment Goals

* Explore how to integrate Claude Code and MCP workflows within the Unity environment.
* Evaluate the speed and accuracy of AI-assisted tools when developing game logic and micro-mechanics.
* Deliver a fully functional mini-game from scratch.

Tech Stack & Tools

* **Engine:** Unity
* **Language:** C#

You play a fire mage defending  against waves of corrupted-forest creatures.

Features

- **Four fire skills**
  - Left click: Ember Bolt. A fast single-target shot whose hits charge your ultimate.
  - E: Fireball. A heavy projectile with splash damage.
  - Q: Nova Blast. An area burst around the player that damages and knocks back enemies.
  - F1: Inferno Beam. A 5-second beam of fire that pierces enemies.
- **Crosshair aiming.** Projectiles and the beam go where the crosshair points, using a ray from the camera.
- **Wave spawner.** Each wave picks a random valid spot on the map and marks it with a beacon before spawning. Waves scale endlessly.
- **Three enemy types.** Sprout (fodder), Thornback (fast swarmer) and Bramble Brute (slow tank with a telegraphed slam). Each has its own health, speed and attack.
- **Skill bar HUD** with cooldowns and an ultimate charge meter.

Notes

Characters, effects and music are made from basic shapes and code, with no external
art or audio assets.
