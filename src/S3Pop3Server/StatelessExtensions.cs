using System;
using Stateless;

namespace S3Pop3Server
{
    public static class StatelessExtensions
    {
        public static void EnsurePermitted<TState, TTrigger>(this StateMachine<TState, TTrigger> machine, TTrigger trigger)
        {
            if (!machine.CanFire(trigger))
            {
                throw new InvalidOperationException();
            }
        }
    }
}
