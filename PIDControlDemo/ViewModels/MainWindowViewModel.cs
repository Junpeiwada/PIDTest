using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;
using PIDControlDemo.Models;

namespace PIDControlDemo.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ThrottleActuator _throttle = new();
    private readonly EngineSimulator _engine = new();
    private readonly PidController _pid = new();
    private readonly RelayAutoTuner _autoTuner = new();
    private readonly SmithPredictor _smith = new();
    private readonly DispatcherTimer _timer;

    private const double SimStepSec = 0.01;            // 10ms シミュレーション刻み
    private const double ControlCycleSec = 2.0;       // 2秒 PID制御周期（センサー遅延に合わせる）
    private const double DisplayIntervalSec = 0.1;    // 100ms UI更新周期
    private const double DeadBand = 15.0;             // PID出力の不感帯(rpm)
    private const double MinPressDuration = 0.1;      // ボタン最短押下時間(秒) = 実機制約
    private const double MaxPressDuration = 10.0;     // ボタン最大押下時間(秒) = 押しっぱなし許容
    private const double OutputToTimeScale = 0.003;   // PID出力→押下時間の変換係数
    private const int MaxGraphPoints = 300;           // 30秒分

    // --- 目標回転数 ---
    [ObservableProperty]
    private double _targetRpm = 1500;

    // --- 検知回転数 ---
    [ObservableProperty]
    private double _sensedRpm;

    // --- アクセル開度 ---
    [ObservableProperty]
    private double _currentThrottle;

    // --- ボタン状態 ---
    [ObservableProperty]
    private bool _isUpPressed;

    [ObservableProperty]
    private bool _isDownPressed;

    // --- PID ON/OFF ---
    [ObservableProperty]
    private bool _isPidEnabled = true;

    // --- PIDゲイン ---
    [ObservableProperty]
    private double _kp = 0.15;

    [ObservableProperty]
    private double _ki = 0.03;

    [ObservableProperty]
    private double _kd = 0.8;

    // --- スミス予測器 ---
    [ObservableProperty]
    private bool _isSmithEnabled = true;

    // --- オートチューン ---
    [ObservableProperty]
    private bool _isAutoTuning;

    [ObservableProperty]
    private string _autoTuneStatus = "";

    [ObservableProperty]
    private double _autoTuneProgress;

    // --- グラフデータ ---
    public List<GraphPoint> GraphPoints { get; } = new();

    [ObservableProperty]
    private int _graphVersion;

    private double _elapsedTime;
    private double _controlTimer;             // 制御周期カウンタ
    private double _pressDurationRemaining;   // 残りボタン押下時間
    private bool _pressDirectionUp;           // true=UP, false=DOWN
    private double _lastPidOutput;            // 前回のPID出力（スミス予測器用）

    // --- 手動操作用フラグ ---
    private bool _manualUpPressed;
    private bool _manualDownPressed;

    public MainWindowViewModel()
    {
        _smith.AutoConfigure();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(DisplayIntervalSec)
        };
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    partial void OnIsPidEnabledChanged(bool value)
    {
        if (value)
        {
            _pid.Reset();
            _manualUpPressed = false;
            _manualDownPressed = false;
        }
    }

    partial void OnKpChanged(double value) => _pid.Kp = value;
    partial void OnKiChanged(double value) => _pid.Ki = value;
    partial void OnKdChanged(double value) => _pid.Kd = value;

    partial void OnIsSmithEnabledChanged(bool value)
    {
        _smith.Reset();
        _pid.Reset();
    }

    // 手動ボタン操作
    [RelayCommand]
    private void ManualUpPress() => _manualUpPressed = true;
    [RelayCommand]
    private void ManualUpRelease() => _manualUpPressed = false;
    [RelayCommand]
    private void ManualDownPress() => _manualDownPressed = true;
    [RelayCommand]
    private void ManualDownRelease() => _manualDownPressed = false;

    // オートチューン
    [RelayCommand]
    private void StartAutoTune()
    {
        if (IsAutoTuning)
        {
            _autoTuner.Cancel();
            IsAutoTuning = false;
            AutoTuneStatus = "";
            AutoTuneProgress = 0;
            IsPidEnabled = true;
            return;
        }

        if (TargetRpm < 300)
        {
            AutoTuneStatus = "目標回転数を300rpm以上に設定してください";
            return;
        }

        IsPidEnabled = false;
        IsAutoTuning = true;
        _pid.Reset();
        _pressDurationRemaining = 0;
        _autoTuner.Start(TargetRpm);
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        _pid.Kp = Kp;
        _pid.Ki = Ki;
        _pid.Kd = Kd;

        // オートチューン中の処理
        if (IsAutoTuning)
        {
            _autoTuner.Update(DisplayIntervalSec, _engine.SensedRpm);
            AutoTuneStatus = _autoTuner.StatusMessage;
            AutoTuneProgress = _autoTuner.Progress;

            if (_autoTuner.State == RelayAutoTuner.TunerState.Done)
            {
                // 計測結果をPIDゲインに反映
                Kp = _autoTuner.ResultKp;
                Ki = _autoTuner.ResultKi;
                Kd = _autoTuner.ResultKd;
                _pid.Reset();
                IsAutoTuning = false;
                IsPidEnabled = true;
            }
        }

        // PID制御判定（制御周期ごと）
        _controlTimer += DisplayIntervalSec;
        if (_controlTimer >= ControlCycleSec && IsPidEnabled && !IsAutoTuning)
        {
            _controlTimer = 0;

            // スミス予測器で補正したプロセス値を使用
            double processValue = _engine.SensedRpm;
            if (IsSmithEnabled)
            {
                processValue = _smith.ComputeCorrectedPV(
                    _engine.SensedRpm, _lastPidOutput, ControlCycleSec);
            }

            double output = _pid.Compute(TargetRpm, processValue, ControlCycleSec);
            _lastPidOutput = output;

            if (Math.Abs(output) > DeadBand)
            {
                // PID出力の大きさに応じてボタン押下時間を決定
                double pressDuration = Math.Abs(output) * OutputToTimeScale;
                pressDuration = Math.Clamp(pressDuration, MinPressDuration, MaxPressDuration);

                _pressDurationRemaining = pressDuration;
                _pressDirectionUp = output > 0;
            }
            else
            {
                _pressDurationRemaining = 0;
            }
        }

        // シミュレーション内部更新 (10ms × 10 = 100ms分)
        int simSteps = (int)(DisplayIntervalSec / SimStepSec);
        for (int i = 0; i < simSteps; i++)
        {
            // PIDとマニュアルのOR合成（PIDはマニュアル操作を検知できない）
            bool pidUp = _pressDurationRemaining > 0 && _pressDirectionUp;
            bool pidDown = _pressDurationRemaining > 0 && !_pressDirectionUp;

            // オートチューンの出力もOR合成
            bool tunerUp = IsAutoTuning && _autoTuner.OutputUp;
            bool tunerDown = IsAutoTuning && _autoTuner.OutputDown;

            bool upPressed = _manualUpPressed || pidUp || tunerUp;
            bool downPressed = _manualDownPressed || pidDown || tunerDown;

            if (_pressDurationRemaining > 0)
                _pressDurationRemaining -= SimStepSec;

            _throttle.Update(SimStepSec, upPressed, downPressed);
            _engine.Update(SimStepSec, _throttle.CurrentThrottle);
        }

        _elapsedTime += DisplayIntervalSec;

        // ボタン状態UI更新
        {
            bool pidUp = _pressDurationRemaining > 0 && _pressDirectionUp;
            bool pidDown = _pressDurationRemaining > 0 && !_pressDirectionUp;
            bool tunerUp = IsAutoTuning && _autoTuner.OutputUp;
            bool tunerDown = IsAutoTuning && _autoTuner.OutputDown;
            IsUpPressed = _manualUpPressed || pidUp || tunerUp;
            IsDownPressed = _manualDownPressed || pidDown || tunerDown;
        }

        SensedRpm = _engine.SensedRpm;
        CurrentThrottle = _throttle.CurrentThrottle;

        GraphPoints.Add(new GraphPoint
        {
            Time = _elapsedTime,
            TargetRpm = TargetRpm,
            SensedRpm = _engine.SensedRpm,
            Throttle = _throttle.CurrentThrottle
        });

        while (GraphPoints.Count > MaxGraphPoints)
        {
            GraphPoints.RemoveAt(0);
        }

        GraphVersion++;
    }
}

public class GraphPoint
{
    public double Time { get; init; }
    public double TargetRpm { get; init; }
    public double SensedRpm { get; init; }
    public double Throttle { get; init; }
}
