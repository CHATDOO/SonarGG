# SonarGG ⚡

> **Table de Mixage Virtuelle Haute Performance & Suite de Routage Audio pour Windows**  
> *High-Performance Virtual Audio Mixer & Audio Routing Suite for Windows*

[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011-0078D6.svg)](https://www.microsoft.com/windows)
[![Architecture](https://img.shields.io/badge/Arch-x64-orange.svg)]()
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

---

## 🌟 Fonctionnalités Principales / Key Features

### 🎚️ Table de Mixage 5 Canaux (Virtual Mixer)
- **Canaux Virtuels Dédiés** :
  - 🎮 **Jeu (Gaming)**
  - 💬 **Chat Vocal (Voice Chat)**
  - 🎵 **Média & Musique (Media)**
  - 🎧 **Auxiliaire (Auxiliary)**
  - 🎙️ **Microphone (Capture)**
- Faders verticaux haute précision avec VU-Mètres balistiques en temps réel (60 FPS).
- Mute instantané avec raccourcis clavier globaux.

### 📦 Routage d'Applications & Multi-Sources (App Routing)
- Détection automatique de toutes les applications en cours d'exécution émettant du son (Discord, Spotify, Steam, Chrome, Jeux...).
- **Routage en 1 clic** : Basculez n'importe quelle application vers le canal désiré grâce aux boutons segmentés rétro-éclairés.
- **Glisser-Déposer (Drag & Drop)** : Glissez vos applications directement dans les colonnes de mixage.
- Défilement horizontal fluide avec la molette de la souris ou les touches rapides `[ ◀ ]` `[ ▶ ]`.
- Contrôle individuel du volume et mute par application.

### ⚖️ ChatMix
- Molette d'équilibrage matériel en temps réel entre le son de votre jeu et Discord.
- Raccourci de réinitialisation rapide à 50/50.

### 🎛️ Égaliseur Paramétrique 10 Bandes
- Courbe de réponse en fréquence interactive temps réel avec affichage de grille dB.
- Profils prédéfinis optimisés pour le gaming compétitif :
  - 🎯 **CS2 / Footsteps Boost** (Bruits de pas amplifiés)
  - 🎯 **Valorant Pro**
  - 🪖 **Call of Duty / Warzone**
  - 🎵 **Deep Bass Boost**
  - 💬 **Clarté de Voix Podcast**

### 🎙️ Studio Micro & Rack VST3
- Chaîne vocale en temps réel avec latence ultra-faible (< 0.5 ms) :
  - **Porte anti-bruit (Noise Gate)** réglable pour éliminer les bruits de fond et claviers mécaniques.
  - **Compresseur vocal studio** pour équilibrer la dynamique de votre voix.
  - **Filtre coupe-bas 80 Hz** anti-pop et anti-résonances de bureau.
  - **Sidetone** (écoute locale directe du micro dans votre casque sans latence).
- **Hôte de Plugins VST3** : Chargez vos effets et égaliseurs externes préférés directement dans votre chaîne vocale.

### 🌐 Spatial Audio 7.1
- Virtualisation sonore 7.1 Surround pour une spatialisation immersive dans les jeux compétitifs et le multimédia.

### 📱 Mode Mini Compact (Mini Mode)
- Interface compacte épurée affichant l'essentiel : les 5 faders, mutes et ChatMix.
- Option **Always-on-Top** (Épingler au premier plan) par-dessus vos jeux en mode fenêtré sans bordure.
- Raccourci rapide `Ctrl + M` pour basculer instantanément entre vue complète et vue mini.

### ⌨️ Raccourcis Clavier Personnalisables (Global Hotkeys)
- Tous les raccourcis sont 100% éditables d'un clic dans l'onglet **Raccourcis**.
- Fonctionnent en jeu plein écran et en arrière-plan.

### 🌐 Bilingue Français / Anglais
- Basculement instantané de la langue dans toute l'interface via le bouton `🌐 FR / EN`.

---

## 🚀 Installation & Compilation

### Prérequis
- **Windows 10 ou Windows 11 (64-bit)**
- **[.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)** (pour compiler) ou **.NET 8.0 Desktop Runtime** (pour exécuter)

### Compilation depuis les sources
```powershell
# Cloner le dépôt
git clone https://github.com/votre-compte/SonarGG.git
cd SonarGG

# Restaurer les dépendances et compiler en mode Release
dotnet build -c Release
```

L'exécutable autonome se trouvera dans :
`bin/Release/net8.0-windows/SonarGG.exe`

---

## 📄 Licence
Ce projet est sous licence [MIT](LICENSE).
