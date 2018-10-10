namespace RedLockNet
{
	public struct RedLockInstanceSummary
	{
		public RedLockInstanceSummary(int acquired, int conflicted, int error)
		{
			this.Acquired = acquired;
			this.Conflicted = conflicted;
			this.Error = error;
		}

		public readonly int Acquired;
		public readonly int Conflicted;
		public readonly int Error;

		public override string ToString()
		{
			return $"Acquired: {Acquired}, Conflicted: {Conflicted}, Error: {Error}";
		}
	}
}