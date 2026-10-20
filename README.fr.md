# SonarGG ⚡

> **Table de Mixage Virtuelle Haute Performance, Routage par Application & Suite DSP pour Windows**  
> *Moteur audio natif C# ultra-faible latence conçu pour les joueurs, streamers et créateurs.*

[![Release](https://img.shields.io/github/v/release/CHATDOO/SonarGG?color=00c853&label=Derni%C3%A8re%20Release)](https://github.com/CHATDOO/SonarGG/releases/latest)
[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Plateforme-Windows%2010%20%2F%2011%20x64-0078D6.svg)](https://www.microsoft.com/windows)
[![License](https://img.shields.io/badge/Licence-MIT-green.svg)](LICENSE)

[🇬🇧 Read in English](README.md)

---

## 🌟 Présentation

**SonarGG** est une table de mixage virtuelle et un gestionnaire de routage audio léger et autonome pour Windows. Il offre un contrôle complet sur vos flux audio avec ChatMix matériel en temps réel, routage par application, égaliseur paramétrique personnalisé, traitement du micro en direct avec hébergement VST3, et un Mode Mini compact épinglable en superposition (Always-on-Top).

---

## 🚀 Fonctionnalités Clés

### 🎚️ Table de Mixage Virtuelle 5 Canaux
- **Canaux Virtuels Dédiés** :
  - 🎮 **Jeu** (`Sonar - Gaming`)
  - 💬 **Chat Vocal** (`Sonar - Chat`)
  - 🎵 **Média & Musique** (`Sonar - Media`)
  - 🎧 **Auxiliaire** (`Sonar - Aux`)
  - 🎙️ **Microphone** (`Sonar - Mic`)
- Faders verticaux haute précision avec VU-mètres balistiques 60 FPS en temps réel.
- Contrôles de mute instantanés avec raccourcis globaux.

### 📦 Routage d'Applications & Multi-Sources
- **Détection automatique des sessions audio** : Détecte toutes les applications actives émettant du son (Discord, Spotify, jeux Steam, Chrome, OBS, etc.).
- **Routage en 1 clic** : Affectez instantanément une application vers le canal souhaité via des sélecteurs segmentés rétro-éclairés.
- **Glisser-Déposer (Drag & Drop)** : Glissez vos applications directement dans les colonnes de mixage.
- **Défilement horizontal fluide** : Naviguez confortablement à la molette ou via les boutons rapides `[ ◀ ]` `[ ▶ ]`.
- Fader de volume et bouton mute individuels par application.

### ⚖️ Balance ChatMix en Temps Réel
- Équilibrage dynamique entre le son de votre jeu et vos communications vocales (Discord, etc.).
- Raccourci global et bouton de réinitialisation instantanée à 50/50.

### 🎛️ Égaliseur Paramétrique 10 Bandes
- Visualiseur de courbe de réponse interactive avec grille dB calibrée.
- Profils prédéfinis optimisés pour le gaming compétitif :
  - 🎯 **CS2 / Footsteps Boost** (Bruits de pas et repérage spatial)
  - 🎯 **Valorant Pro**
  - 🪖 **Warzone / CoD Tactique**
  - 🎵 **Deep Bass Boost**
  - 💬 **Clarté Vocale Broadcast**

### 🎙️ Traitement Micro DSP & Rack VST3
- **Chaîne vocale ultra-faible latence (< 0.5 ms)** :
  - **Noise Gate** : Seuil réglable pour supprimer les bruits de frappe clavier et bruits de fond.
  - **Compresseur Studio** : Harmonise la dynamique de la voix (chuchotements et cris).
  - **Filtre Passe-Haut 80 Hz** : Élimine les bruits sourds de bureau et bruits de bouche.
  - **Sidetone zéro latence** : Écoute locale directe de votre voix dans votre casque.
- **Hébergement VST3** : Chargez vos plugins d'effets audio tiers préférés directement dans la chaîne microphone.

### 🌐 Audio Spatial Virtuel 7.1
- Simulation surround 360° immersive optimisée pour les casques de jeu compétitif.

### 📱 Mode Mini Compact & Superposition (Always-on-Top)
- Interface compacte affichant uniquement l'essentiel : les 5 faders, mutes et la molette ChatMix.
- **Always-on-Top** : Épinglez le mixer par-dessus vos jeux en mode fenêtré sans bordure.
- Raccourci rapide de bascule : `Ctrl + M`.

### ⌨️ Raccourcis Clavier Globaux Personnalisables
- Éditez et personnalisez tous les raccourcis directement dans l'onglet **Raccourcis**.
- Fonctionnent en plein écran en jeu et en arrière-plan.

### 🌍 Interface Bilingue (Français / Anglais)
- Basculement instantané de la langue de toute l'interface en 1 clic via le bouton `🌐 FR / EN`.

### 🔄 Mises à Jour Automatiques Intégrées
- Vérification des releases GitHub au démarrage ou manuellement dans l'onglet **Paramètres**.
- Téléchargement intégré, jauge de progression et remplacement automatique sans intervention technique.

---

## 📥 Téléchargement & Installation

### Option 1 : Exécutable Pré-compilé (Recommandé)
1. Téléchargez la dernière version `SonarGG.exe` ou `SonarGG-Windows-x64.zip` depuis les [Releases GitHub](https://github.com/CHATDOO/SonarGG/releases/latest).
2. Lancez `SonarGG.exe`. Aucune installation n'est requise.

### Option 2 : Compilation depuis les Sources

#### Prérequis
- **Windows 10 / 11 (64-bit)**
- **[.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)**

#### Compilation
```powershell
# Cloner le dépôt
git clone https://github.com/CHATDOO/SonarGG.git
cd SonarGG

# Compiler en mode Release
dotnet build -c Release
```

L'exécutable compilé sera disponible dans :
`bin/Release/net8.0-windows/SonarGG.exe`

---

## 📄 Licence

Ce projet est sous licence [MIT](LICENSE).
