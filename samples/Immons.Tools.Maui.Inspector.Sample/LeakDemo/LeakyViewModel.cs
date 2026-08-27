using System.ComponentModel;

namespace SampleApp.LeakDemo;

/// <summary>Subscribes to the static event in its constructor and never unsubscribes — leaks with its page.</summary>
public sealed class LeakyViewModel : INotifyPropertyChanged
{
	static int _created;
	int _ticks;

	public LeakyViewModel()
	{
		Number = ++_created;
		LeakSource.Tick += OnTick;
	}

	public int Number { get; }

	public string Title => $"Leaky page #{Number}";

	public int Ticks
	{
		get => _ticks;
		private set
		{
			_ticks = value;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Ticks)));
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	void OnTick(object? sender, EventArgs e) => Ticks++;
}
