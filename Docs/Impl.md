# エンジン回転数PID制御デモ 実装計画

## フェーズ1: プロジェクトセットアップ

### 1.1 Avaloniaプロジェクト作成
- `dotnet new avalonia.mvvm -o PIDControlDemo` でプロジェクト作成
- .NET 8ターゲット確認
- CommunityToolkit.Mvvm NuGetパッケージ追加（MVVM支援）
- ソリューションファイル作成

### 1.2 プロジェクト構造
```
PIDControlDemo/
├── Models/
│   ├── EngineSimulator.cs
│   ├── ThrottleActuator.cs
│   └── PidController.cs
├── ViewModels/
│   └── MainViewModel.cs
├── Views/
│   ├── MainWindow.axaml
│   └── MainWindow.axaml.cs
├── Controls/
│   └── RealtimeGraphControl.cs
├── App.axaml
├── App.axaml.cs
└── Program.cs
```

## フェーズ2: モデル層の実装

### 2.1 ThrottleActuator（アクセル開度アクチュエータ）
- プロパティ: `CurrentThrottle` (double, 0~100)
- メソッド: `Update(double deltaTime, bool isUpPressed, bool isDownPressed)`
  - UP押下中: `+= ChangeRate * deltaTime`（ChangeRate = 10%/秒）
  - DOWN押下中: `-= ChangeRate * deltaTime`
  - `Math.Clamp(0, 100)` で上下限制限
- 最もシンプルなクラスなので最初に実装

### 2.2 EngineSimulator（エンジンシミュレーション）
- **非線形トルクカーブ**: ルックアップテーブル + 線形補間
  - `double[] ThrottlePoints = { 0, 10, 20, ..., 100 }`
  - `double[] RpmPoints = { 0, 150, 400, ..., 3000 }`
  - `GetSteadyStateRpm(double throttle)` で補間算出
- **一次遅れ系**: `actualRpm += (targetRpm - actualRpm) * (1 - exp(-dt / timeConstant))`
  - timeConstant: 約1.0秒（応答速度の調整パラメータ）
- **センサー遅延**: `Queue<(double time, double rpm)>` でバッファリング
  - 2秒前の回転数を `SensedRpm` として返す
- メソッド: `Update(double deltaTime, double currentThrottle)`
- プロパティ: `ActualRpm`, `SensedRpm`

### 2.3 PidController（PID制御器）
- プロパティ: `Kp`, `Ki`, `Kd`（外部から変更可能）
- 内部状態: `integral`, `previousError`
- メソッド: `Compute(double setpoint, double processValue, double deltaTime) → double`
  - 偏差 = setpoint - processValue
  - P項 = Kp * 偏差
  - I項 = Ki * integral（クランプ付き）
  - D項 = Kd * (偏差 - 前回偏差) / deltaTime
  - 出力 = P + I + D
- メソッド: `Reset()` — ON/OFF切替時に積分値をリセット
- アンチワインドアップ: 積分値を ±1000 にクランプ
- 出力の解釈（ViewModel側）:
  - 出力 > 不感帯閾値 → UP押下
  - 出力 < -不感帯閾値 → DOWN押下
  - それ以外 → 両方リリース

## フェーズ3: ViewModel層の実装

### 3.1 MainViewModel
- **バインディングプロパティ**:
  - `TargetRpm` (double) — 目標回転数スライダー
  - `SensedRpm` (double) — 検知回転数表示
  - `CurrentThrottle` (double) — アクセル開度表示
  - `IsUpPressed` (bool) — UPボタン状態
  - `IsDownPressed` (bool) — DOWNボタン状態
  - `IsPidEnabled` (bool) — PID ON/OFF
  - `Kp`, `Ki`, `Kd` (double) — PIDゲインスライダー
  - `GraphData` — グラフ用データコレクション
- **制御ループ**: `DispatcherTimer` 100ms周期
  1. シミュレーション内部更新（10ms刻み × 10回 = 100ms分）
  2. PID ON時: PID演算 → ボタン状態を自動設定
  3. UIプロパティ更新
  4. グラフデータ追加
- **手動操作**: PID OFF時のみ、マウスダウン/アップでボタン状態を変更

## フェーズ4: View層の実装

### 4.1 MainWindow（AXAML）
- **上部**: 目標回転数スライダー（0~3000rpm）
- **中部**: 検知回転数バー、アクセル開度バー、ボタン状態表示
- **制御パネル**: PID ON/OFFトグル、PIDゲインスライダー×3
- **手動操作**: UP/DOWNボタン（PointerPressed/PointerReleased で長押し検出）
- **下部**: リアルタイムグラフ
- レイアウト: `DockPanel` + `StackPanel` を組み合わせたシンプル構成

### 4.2 ボタンの長押し実装
- `PointerPressed` イベント → `IsUpPressed = true`
- `PointerReleased` イベント → `IsUpPressed = false`
- `PointerCaptureLost` イベント → `IsUpPressed = false`（安全対策）
- PID ON時はボタンの `IsEnabled = false` で手動操作を無効化
- PID制御中のボタンハイライト: ボタンのスタイルを `IsUpPressed` にバインド

## フェーズ5: リアルタイムグラフの実装

### 5.1 RealtimeGraphControl（カスタムコントロール）
- Avaloniaの `Control` を継承し、`Render` メソッドで `DrawingContext` に直接描画
- サードパーティライブラリは使わず自前描画（依存を最小化）

### 5.2 データ管理
- `List<GraphPoint>` で直近30秒分のデータを保持
  - `GraphPoint { Time, TargetRpm, SensedRpm, Throttle }`
- 100ms間隔で追加（30秒 = 300ポイント）
- 300ポイントを超えたら古いデータを削除

### 5.3 描画内容
- 背景: ダークグレー
- グリッド線: 薄いグレー（Y軸: 500rpm刻み、X軸: 5秒刻み）
- 目標回転数ライン: 青、太さ2px
- 検知回転数ライン: 赤、太さ2px
- アクセル開度ライン: 緑、太さ1px（右Y軸スケール）
- 軸ラベル: 左Y軸「rpm」、右Y軸「%」
- 凡例: グラフ上部に色付きラベル

## フェーズ6: 結合・チューニング

### 6.1 結合テスト
- 手動モードでUP/DOWNボタン操作 → 開度変化 → 回転数変化を確認
- 非線形特性が正しく反映されているか確認
- センサー遅延（2秒）が正しく動作するか確認
- グラフに3系列が正しく描画されるか確認

### 6.2 PIDチューニング
- まずKpのみで大まかに追従させる
- Kiを加えて定常偏差を除去
- Kdを加えてオーバーシュートを抑制
- 非線形特性による各動作点でのゲイン感度の違いを確認
- 初期値をSpec.mdに反映

### 6.3 UI仕上げ
- ボタン押下時のハイライトアニメーション
- 数値表示のフォーマット（小数1桁）
- ウィンドウサイズ・レイアウトの調整

## 実装順序（依存関係順）

```
1. プロジェクトセットアップ
   ↓
2. ThrottleActuator（依存なし）
   ↓
3. EngineSimulator（ThrottleActuatorの出力を入力に使う）
   ↓
4. PidController（依存なし、単体テスト可能）
   ↓
5. MainViewModel（全モデルを統合）
   ↓
6. MainWindow AXAML（バー、スライダー、ボタン部分）
   ↓
7. RealtimeGraphControl（カスタム描画）
   ↓
8. 結合・チューニング
```

## 見積もり対象外

- ユニットテスト（必要に応じて追加）
- CI/CD設定
- アイコン・スプラッシュスクリーン
