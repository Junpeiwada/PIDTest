using System;

namespace PIDControlDemo.Models;

public class ThrottleActuator
{
    public const double MinThrottle = 0.0;
    public const double MaxThrottle = 100.0;
    public const double ChangeRatePerSecond = 10.0; // %/秒

    public double CurrentThrottle { get; private set; }

    public void Update(double deltaTime, bool isUpPressed, bool isDownPressed)
    {
        if (isUpPressed && !isDownPressed)
        {
            CurrentThrottle += ChangeRatePerSecond * deltaTime;
        }
        else if (isDownPressed && !isUpPressed)
        {
            CurrentThrottle -= ChangeRatePerSecond * deltaTime;
        }

        CurrentThrottle = Math.Clamp(CurrentThrottle, MinThrottle, MaxThrottle);
    }

    public void Reset()
    {
        CurrentThrottle = 0.0;
    }
}
