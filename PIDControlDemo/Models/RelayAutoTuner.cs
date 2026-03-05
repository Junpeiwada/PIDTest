using System;
using System.Collections.Generic;

namespace PIDControlDemo.Models;

/// <summary>
/// リレーフィードバック法によるPIDオートチューニング。
/// 目標値の上下でUP/DOWNを交互に切り替え、発振の周期と振幅を計測し、
/// Ziegler-Nichols法でクリティカルダンピングに近いPIDゲインを算出する。
/// </summary>
public class RelayAutoTuner
{
    public enum TunerState
    {
        Idle,
        WaitingForStable,   // 初期安定待ち
        Relaying,           // リレー制御中（発振計測）
        Done                // 完了
    }

    // リレー制御パラメータ
    private const double RelayPressDuration = 1.5;  // リレーのON時間(秒)
    private const int RequiredCrossings = 6;        // 必要なゼロクロス数（3周期分）
    private const double StabilizeTime = 6.0;       // 初期安定待ち時間(秒)

    public TunerState State { get; private set; } = TunerState.Idle;
    public string StatusMessage { get; private set; } = "";
    public double Progress { get; private set; }

    // 計測結果
    public double ResultKp { get; private set; }
    public double ResultKi { get; private set; }
    public double ResultKd { get; private set; }

    // ボタン出力
    public bool OutputUp { get; private set; }
    public bool OutputDown { get; private set; }

    private double _targetRpm;
    private double _elapsed;
    private double _relayTimer;
    private bool _relayDirectionUp;

    // ゼロクロス計測用
    private readonly List<double> _crossingTimes = new();
    private readonly List<double> _peakValues = new();
    private double _lastError;
    private bool _hasLastError;
    private double _currentPeak;
    private bool _trackingPositivePeak;

    public void Start(double targetRpm)
    {
        State = TunerState.WaitingForStable;
        _targetRpm = targetRpm;
        _elapsed = 0;
        _relayTimer = 0;
        _relayDirectionUp = true;
        _crossingTimes.Clear();
        _peakValues.Clear();
        _lastError = 0;
        _hasLastError = false;
        _currentPeak = 0;
        _trackingPositivePeak = true;
        OutputUp = false;
        OutputDown = false;
        StatusMessage = "安定待ち中...";
        Progress = 0;

        ResultKp = 0;
        ResultKi = 0;
        ResultKd = 0;
    }

    public void Cancel()
    {
        State = TunerState.Idle;
        OutputUp = false;
        OutputDown = false;
        StatusMessage = "";
        Progress = 0;
    }

    /// <summary>
    /// 制御周期ごとに呼ぶ（displayInterval単位）。
    /// sensedRpmは現在のセンサー値。
    /// </summary>
    public void Update(double deltaTime, double sensedRpm)
    {
        if (State == TunerState.Idle || State == TunerState.Done)
            return;

        _elapsed += deltaTime;

        if (State == TunerState.WaitingForStable)
        {
            // 最初にUPを押して目標付近まで持っていく
            OutputUp = true;
            OutputDown = false;
            Progress = Math.Min(_elapsed / StabilizeTime, 0.15);

            if (_elapsed >= StabilizeTime)
            {
                State = TunerState.Relaying;
                _elapsed = 0;
                _relayTimer = 0;
                OutputUp = false;
                OutputDown = false;
                StatusMessage = "リレー制御で発振計測中...";
            }
            return;
        }

        // --- Relaying ---
        double error = _targetRpm - sensedRpm;

        // リレー制御: 誤差の符号に応じてUP/DOWNを切り替え
        // ただし最低RelayPressDuration秒は同じ方向を維持
        _relayTimer += deltaTime;
        if (_relayTimer >= RelayPressDuration)
        {
            bool shouldBeUp = error > 0;
            if (shouldBeUp != _relayDirectionUp)
            {
                _relayDirectionUp = shouldBeUp;
                _relayTimer = 0;
            }
        }

        OutputUp = _relayDirectionUp;
        OutputDown = !_relayDirectionUp;

        // ゼロクロス検出
        if (_hasLastError)
        {
            bool crossed = (_lastError > 0 && error <= 0) || (_lastError < 0 && error >= 0);
            if (crossed)
            {
                _crossingTimes.Add(_elapsed);

                // ピーク値を記録
                _peakValues.Add(Math.Abs(_currentPeak));
                _currentPeak = 0;
                _trackingPositivePeak = error > 0;
            }

            // ピークトラッキング
            if (Math.Abs(error) > Math.Abs(_currentPeak))
            {
                _currentPeak = error;
            }
        }
        _lastError = error;
        _hasLastError = true;

        // 進捗更新
        double relayCrossProgress = Math.Min((double)_crossingTimes.Count / RequiredCrossings, 1.0);
        Progress = 0.15 + relayCrossProgress * 0.85;
        StatusMessage = $"リレー制御で発振計測中... ({_crossingTimes.Count}/{RequiredCrossings} クロス)";

        // 十分なゼロクロスが得られたら計算
        if (_crossingTimes.Count >= RequiredCrossings)
        {
            CalculateGains();
            OutputUp = false;
            OutputDown = false;
            State = TunerState.Done;
            Progress = 1.0;
        }
    }

    private void CalculateGains()
    {
        // 周期Tuの計算: ゼロクロス間隔の平均 × 2（半周期→全周期）
        var intervals = new List<double>();
        for (int i = 1; i < _crossingTimes.Count; i++)
        {
            intervals.Add(_crossingTimes[i] - _crossingTimes[i - 1]);
        }

        double avgHalfPeriod = 0;
        foreach (var interval in intervals)
            avgHalfPeriod += interval;
        avgHalfPeriod /= intervals.Count;

        double tu = avgHalfPeriod * 2; // 全周期

        // 振幅Auの計算: ピーク値の平均
        double avgAmplitude = 0;
        foreach (var peak in _peakValues)
            avgAmplitude += peak;
        avgAmplitude /= _peakValues.Count;

        // 限界ゲインKu: リレー振幅からの推定
        // リレーフィードバックでは Ku = 4d / (π × Au)
        // d = リレー出力の振幅（ここではボタン押下による変化率相当）
        // 簡易的にボタン操作の効果を推定: 10%/s × RelayPressDuration × エンジン特性
        // 実用的にはAu（rpm振幅）とTuから直接Ziegler-Nichols計算
        double relayAmplitude = 300; // リレー操作のrpm換算効果（推定値）
        double ku = 4.0 * relayAmplitude / (Math.PI * avgAmplitude);

        if (ku < 0.01) ku = 0.01;
        if (tu < 0.5) tu = 2.0;

        // Ziegler-Nichols PID（"no overshoot"チューニング - よりダンピング重視）
        // 標準ZN: Kp=0.6Ku, Ki=Kp/(0.5Tu), Kd=Kp*Tu/8
        // ダンピング重視: Kp=0.33Ku, Ki=Kp/(0.5Tu), Kd=Kp*Tu/3
        ResultKp = 0.33 * ku;
        ResultKi = ResultKp / (0.5 * tu);
        ResultKd = ResultKp * tu / 3.0;

        // このシステムの出力スケール(OutputToTimeScale=0.003)に合わせてスケーリング
        // PID出力 → ボタン押下時間の変換があるため、ゲインを適切な範囲に収める
        ResultKp = Math.Clamp(ResultKp, 0.01, 5.0);
        ResultKi = Math.Clamp(ResultKi, 0.001, 2.0);
        ResultKd = Math.Clamp(ResultKd, 0.0, 2.0);

        // 小数2桁に丸め
        ResultKp = Math.Round(ResultKp, 2);
        ResultKi = Math.Round(ResultKi, 2);
        ResultKd = Math.Round(ResultKd, 2);

        StatusMessage = $"完了! Tu={tu:F1}s, 振幅={avgAmplitude:F0}rpm → Kp={ResultKp:F2}, Ki={ResultKi:F2}, Kd={ResultKd:F2}";
    }
}
