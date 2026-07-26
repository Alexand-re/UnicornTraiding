# 🚀 Guide d'Utilisation : Bot cTrader Intraday Prop Firm (`InstitutionalIntradayEngineBot`)

## 📌 Présentation
Le **`InstitutionalIntradayEngineBot`** est un robot de trading C# natif conçu sur mesure pour passer et maintenir les comptes de challenges Prop Firm (**cTrader Automate**, **FTMO**, **MyForexFunds**, etc.).

Fichiers sources et exécutables cTrader :
- **Fichier source C#** : [InstitutionalIntradayEngineBot.cs](file:///Users/alexperso/RiderProjects/UnicornTraiding/InstitutionalIntradayEngineBot/InstitutionalIntradayEngineBot.cs)
- **Executable cTrader `.algo`** : `~/cAlgo/Sources/Robots/UnicornTraiding.algo`

---

## ⏰ Fenêtre Horaire d'Émission des Trades

Les trades sont exécutés exclusivement durant la fenêtre de liquidité institutionnelle US :
- **14h30 UTC** : Début du calcul de l'Opening Range (ORB 15m).
- **14h45 UTC (9h45 EST)** : **Début de l'émission des trades** (dès la formation du niveau ORB High/Low).
- **21h00 UTC (16h00 EST)** : **Arrêt de l'émission de nouveaux ordres**.
- **21h45 UTC (16h45 EST)** : **Liquidation de force EOD** (0 position overnight).

---

## ⚖️ Gestion de Risque & Levier (Jusqu'à 4.0x)

| Règle / Protection | Valeur cTrader / FTMO | Garde-Fou Interne du Bot | Action Automatique |
|---|---|---|---|
| **Max Absolute Drawdown** | **10.0%** | **8.5%** | Verrouillage total (`Abs Lockout`). Bloque tout nouveau trade. |
| **Max Daily Loss Limit** | **4.5%** ou **5.0%** | **3.8%** | Liquidation immédiate & Verrouillage de la journée (`Daily Lockout`). |
| **Levier Max Autorisé** | **Jusqu'à 4.0x** | Configurable (`MaxLeverageMultiplier = 4.0`) | Plafonne automatiquement l'exposition par position. |
| **Risque par Trade** | Variable | **0.5%** (Base) réactif au DD | Réduit automatiquement le risque au fur et à mesure du Drawdown. |
| **Exposition Overnight** | Interdite / Risquée | **0 Position** | Liquidation de force 100% des positions à **21h45 UTC**. |

### Formule de Sizing Dynamique
Le bot calcule le volume exact à risquer selon le buffer de Drawdown restant et le levier max :
$$\text{Volume (Actions)} = \min\left(\frac{\text{Capital Risqué (\$)}}{\text{Prix d'Entrée} - \text{Stop Loss}}, \frac{\text{Capital} \times \text{LevierMax}}{\text{Prix d'Entrée}}\right)$$

---

## 📈 Les 3 Setups d'Entrée Institutionnels

1. **Setup A — VWAP Pullback Continuation (Trend Following)** :
   - *Condition* : Prix au-dessus de l'Opening Range (ORB 15m) + Prix au-dessus du VWAP.
   - *Entrée* : Repli sur le VWAP avec bougie de rejet haussière.
   - *Stop Loss* : Sous le VWAP / mèche précédente ($-1.0$ ATR).
   - *Take Profit* : $R:R \ge 2.0:1$.

2. **Setup B — Liquidity Sweep & Reversal (Chasse aux stops institutionnelle)** :
   - *Condition* : Le prix casse temporairement le plus haut/bas de l'ORB 15m mais réintègre la zone dans les 2 bougies de 5m.
   - *Entrée* : Réintégration immédiate du niveau.
   - *Stop Loss* : Au-dessus/en-dessous du pic du sweep (+0.2 ATR).
   - *Take Profit* : VWAP opposé ($R:R \ge 2.0:1$).

3. **Setup C — ORB Expansion Breakout** :
   - Cassure franche avec volume massif de l'ORB 15m.

---

## 🛠️ Trade Management en Temps Réel

- **Breakeven à $+1.5R$** : Dès qu'une position atteint $+1.5\times$ son risque initial, le Stop Loss est immédiatement déplacé au Prix d'Entrée ($0.00\$ \text{ de risque}$).
- **Trailing Stop VWAP à $+2.0R$** : Dès $+2.0R$, le Stop Loss suit le niveau dynamique du VWAP pour sécuriser les profits.

---

## 📥 Guide d'Installation dans cTrader Automate

### Étape 1 : Charger le Bot
1. Ouvrez **cTrader**.
2. Allez dans l'onglet **Automate** (panneau de gauche).
3. Le bot `InstitutionalIntradayEngineBot` apparaît déjà sous **Robots** (détecté automatiquement via le fichier `.algo` copié dans `~/cAlgo/Sources/Robots/`).

### Étape 2 : Configurer les Paramètres

```ini
[Prop Firm Risk Management]
Max Absolute DD Limit (%) = 8.5
Max Daily Loss Limit (%)  = 3.8
Base Risk per Trade (%)   = 0.5
Max Leverage Multiplier   = 4.0

[Timing]
Start Trading Hour (UTC)     = 14
Start Trading Minute (UTC)   = 45
Stop New Trades Hour (UTC)   = 21
EOD Force Close Hour (UTC)   = 21
EOD Force Close Minute (UTC) = 45

[Strategy Setups]
Opening Range Minutes = 15
Min Risk/Reward Ratio = 2.0
Breakeven Trigger (R) = 1.5
```

### Étape 3 : Lancer le Bot
1. Cliquez sur **`Play ▶`** pour démarrer le bot.
