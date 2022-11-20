<Query Kind="Program">
  <NuGetReference>NAudio</NuGetReference>
  <Namespace>System.Drawing</Namespace>
  <Namespace>System.Numerics</Namespace>
  <Namespace>NAudio.Wave</Namespace>
  <Namespace>NAudio.Wave.SampleProviders</Namespace>
</Query>

string SilenceTrack = Path.Combine(Path.GetDirectoryName(Util.CurrentQueryPath), @"silence.mp3");
string MusicTrack = Path.Combine(Path.GetDirectoryName(Util.CurrentQueryPath), @"the-beat-of-nature-122841.mp3");
string ColourScale = Path.Combine(Path.GetDirectoryName(Util.CurrentQueryPath), @"viridisScale.png");
//string Output = Path.Combine(Path.GetDirectoryName(Util.CurrentQueryPath), @"output.mp3");

void Main()
{
	var duration = 0;
	List<float> volumeLookup;
	using (var rd = new AudioFileReader(MusicTrack))
	{
		duration = (int)rd.TotalTime.TotalSeconds;
		volumeLookup = GetVolume(rd);
	}

	var listener = new Vector2(25, 15);
	var origin = new Vector2(50, 50);
	var playlist = new List<List<(int SoundIndex, float Volume)>>(); // this is a second by second record of audio packets that pass the listener
	var particles = new List<Particle>(131_072); // initialize to hold a huge amount of data
	var colliders = new[]
	{
		// Living
		new Wall {Point1 = new Vector2(0, 0), Point2 = new Vector2(60, 0), Material = Material.Glass},
		new Wall {Point1 = new Vector2(60, 0), Point2 = new Vector2(60, 37), Material = Material.PaintedBrick},
		new Wall {Point1 = new Vector2(0, 0), Point2 = new Vector2(0, 52), Material = Material.PaintedBrick},
		new Wall {Point1 = new Vector2(0, 52), Point2 = new Vector2(32, 52), Material = Material.PaintedBrick},
		new Wall {Point1 = new Vector2(60, 48), Point2 = new Vector2(60, 54), Material = Material.PaintedBrick},
		new Wall {Point1 = new Vector2(56, 52), Point2 = new Vector2(60, 52), Material = Material.PaintedBrick},
		
		// Dining
		new Wall {Point1 = new Vector2(60, 32), Point2 = new Vector2(99, 32), Material = Material.PaintedBrick},
		
		// Kitchen
		new Wall {Point1 = new Vector2(99, 32), Point2 = new Vector2(99, 99), Material = Material.PaintedBrick},
		new Wall {Point1 = new Vector2(99, 99), Point2 = new Vector2(78, 99), Material = Material.PaintedBrick},
		new Wall {Point1 = new Vector2(78, 108), Point2 = new Vector2(78, 72), Material = Material.PaintedBrick},
		
		// Entrance / Hall
		new Wall {Point1 = new Vector2(78, 72), Point2 = new Vector2(48, 72), Material = Material.PaintedBrick},
		new Wall {Point1 = new Vector2(25, 72), Point2 = new Vector2(25, 52), Material = Material.Hardwood},
		
		// Bed 2
		new Wall {Point1 = new Vector2(38, 72), Point2 = new Vector2(38, 122), Material = Material.PaintedBrick},
		new Wall {Point1 = new Vector2(38, 122), Point2 = new Vector2(0, 122), Material = Material.PaintedBrick},
		new Wall {Point1 = new Vector2(0, 122), Point2 = new Vector2(0, 72), Material = Material.PaintedBrick},
		new Wall {Point1 = new Vector2(0, 72), Point2 = new Vector2(25, 72), Material = Material.PaintedBrick},
		
		// Office
		new Wall {Point1 = new Vector2(38, 122), Point2 = new Vector2(52, 122), Material = Material.PaintedBrick},
		new Wall {Point1 = new Vector2(52, 122), Point2 = new Vector2(52, 108), Material = Material.PaintedBrick},
		new Wall {Point1 = new Vector2(52, 108), Point2 = new Vector2(78, 108), Material = Material.PaintedBrick},
	};

	using var colours = new Colours(ColourScale); // converts percentage to a value on a colour scale
	
	var progress = new DumpContainer("").Dump();

	using var bmp = new Bitmap(100, 120);
	using var gfx = Graphics.FromImage(bmp);
	var dc = new DumpContainer(bmp).Dump("Sound Wave Propogation");
	
	using var bmp2 = new Bitmap(100, 120);
	using var gfx2 = Graphics.FromImage(bmp2);
	var dc2 = new DumpContainer(bmp2).Dump("Volume Heatmap");

	var si = 0; // to prevent silence at the start of the track we want to keep an index of the first sound to hit the listener

	// emit particles / waves
	for (int t = 0; t < duration; t++)
	{
		progress.Content = $"{((float)t / duration):P}";
		var colour = colours.Next();
		for (int d = 0; d < 360; d++)
		{
			var p = new Particle();
			p.Location = p.Rotate(d);
			p.Velocity = p.Location;
			p.Location += origin;

			p.Volume = 1f;
			p.SoundIndex = t;
			p.Colour = colours.Scale(volumeLookup[t]);

			particles.Add(p);
		}

		// Update state
		foreach (var p in particles)
		{
			// update location
			p.Location += p.Velocity;

			// dB to volume % - https://calculator.academy/db-to-percentage-calculator
			// volume decreases with the inverse square of the distance
			var soundPressureLossPerM = 20f * (float)Math.Log10(2);
			var volumeReduction = (float)(Math.Pow(10, soundPressureLossPerM * p.Velocity.Length() / 10) / 100);
			p.Volume -= (volumeReduction / 50);

			// collision with walls
			foreach (var c in colliders.Where(c => c.HasCollided(p)))
			{
				var vector = c.Point2 - c.Point1;
				var normal = Vector2.Normalize(new Vector2(vector.Y, -vector.X)); // clockwise normal
				p.Velocity = Vector2.Reflect(p.Velocity, normal);

				p.Volume *= 1 - (c.Material.AbsorbtionCoefficent * 20);
			}

			// received sound - add each second of sound at listner position to a playlist
			if (Vector2.Distance(listener, p.Location) < p.Radius)
			{
				if (si == 0) si = t;
				var index = (t - si);
				// create a new collection for each timestep if doesnt exist
				if (playlist.Count <= index) playlist.Add(new List<(int, float)>());
				// dont add duplicate sounds for the same timestep
				if (playlist[index].Any(x => x.SoundIndex == p.SoundIndex)) continue;
				// not sure why this jank is required - fixing some duplicate track bug
				if (playlist.Count != 1 && playlist[index - 1].Any(x => x.SoundIndex == p.SoundIndex)) continue;
				playlist[index].Add((p.SoundIndex, p.Volume));
			}
		}
		// sound decay
		particles.RemoveAll(x => x.Volume < 0.01); // remove anything at less than 1% volume

		DrawSoundwaves(gfx, particles, listener);

		DrawHeatmap(gfx2, particles, listener);

		DrawRoom(gfx, gfx2, colliders);

		progress.Refresh();
		dc.Refresh();
		dc2.Refresh();
	}

	// construct audio track by mixing audio heard at listener position
	using var silence = new Mp3FileReader(SilenceTrack);
	var audioTrack = (ISampleProvider)new ConcatenatingSampleProvider(new[] { silence.ToSampleProvider() });
	using var reader = new Mp3FileReader(MusicTrack);
	foreach (var p in playlist)
	{
		var mixer = new MixingSampleProvider(new[] { silence.ToSampleProvider() });
		foreach (var s in p)
		{
			var durationSec = 1;
			reader.Skip(s.SoundIndex); // each index accounts for on second of distance traveled by sound wave - skip s seconds into track
			var sample = CreateSample(reader, s.Volume, durationSec);
			mixer.AddMixerInput(sample);
			reader.Position = 0;
		}
		audioTrack = audioTrack.FollowedBy(mixer);
	}

	//// save results
	//File.Delete(Output);
	//WaveFileWriter.CreateWaveFile(Output, audioTrack.ToWaveProvider());

	// draw the resulting audio track wave
	//using var outputReader = new WaveFileReader(Output);
	using var outputReader = new Mp3FileReader(MusicTrack);
	using var waveImage = new Bitmap(7_500, 200);
	using var waveGfx = Graphics.FromImage(waveImage);
	{
		DrawAudiowave(reader, waveGfx, waveImage.Size);
		waveImage.Dump("Sound Wave");
	}

	// play the final audio track
	using var waveOut = new WaveOut();
	{
		silence.Position = 0;
		waveOut.Init(audioTrack);
		waveOut.Play();
		while (waveOut.PlaybackState == PlaybackState.Playing)
		{
			Thread.Sleep(100);
		}
	}
}

public class Material
{
	// via https://www.engineeringtoolbox.com/accoustic-sound-absorption-d_68.html
	public static Material AcousticTiles = new(0.08f);
	public static Material Asbestos = new(0.07f);
	public static Material PaintedBrick = new(0.02f);
	public static Material UnpaintedBrick = new(0.05f);
	public static Material Carpet = new(0.06f);
	public static Material PaintedConcrete = new(0.04f);
	public static Material UnpaintedConcrete = new(0.07f);
	public static Material Fiberboard = new(0.04f);
	public static Material Hardwood = new(0.3f);
	public static Material Plywood = new(0.02f);
	public static Material Glass = new(0.2f);
	public static Material Plaster = new(0.03f);
	public static Material Foam = new(0.95f);
	public static Material Rubber = new(0.2f);
	
	public float AbsorbtionCoefficent {get; private set;}
	private Material(float absorbtionCoefficient)
	{
		AbsorbtionCoefficent = absorbtionCoefficient;
	}
}

// gets the volume of each second of audio
public List<float> GetVolume(AudioFileReader reader)
{
	var volume = new List<float>();
	int read;
	var samplesPerSecond = reader.WaveFormat.SampleRate * reader.WaveFormat.Channels;
	var buffer = new float[samplesPerSecond];
	do
	{
		read = reader.Read(buffer, 0, buffer.Length);
		if (read == 0) break;
		
		var abs = buffer.Take(read).Max(x => Math.Abs(x));
		volume.Add(abs);

	} while (read > 0);
	
	return volume;
}

public void DrawRoom(Graphics gfx, Graphics gfx2, IEnumerable<Wall> walls)
{
	foreach (var c in walls)
	{
		c.Draw(gfx);
		c.Draw(gfx2);
	}
}

public void DrawSoundwaves(Graphics gfx, List<Particle> particles, Vector2 listener)
{
	gfx.Clear(Color.CornflowerBlue);
	foreach (var p in particles.GroupBy(x => x.SoundIndex).Where((_, i) => i % 3 == 0)) // only draw every other particle, so we see can wave reflections
	{
		foreach (var p2 in p)
			p2.Draw(gfx);
	}
	gfx.FillEllipse(new SolidBrush(Color.Green), listener.X, listener.Y, 4, 4);
}

public void DrawHeatmap(Graphics gfx, List<Particle> particles, Vector2 listener)
{
	gfx.Clear(Color.CornflowerBlue);
	for (int y = 0; y < 10; y++)
		for (int x = 0; x < 10; x++)
		{
			var area = new Rectangle(x * 10, y * 10, 10, 10);
			var volume = particles
				.Where(p => area.Contains((int) p.Location.X, (int) p.Location.Y))
				.Sum(x => x.Volume);
			var colour = Color.FromArgb((short) Math.Min(255, volume / 3), Color.Red);
			var radius = 10;
			gfx.FillEllipse(new SolidBrush(colour), (x * 10) - 5, (y * 10) - 5, radius*2, radius*2);
		}
	gfx.FillEllipse(new SolidBrush(Color.Green), listener.X, listener.Y, 4, 4);
}

public void DrawAudiowave(WaveStream waveStream, Graphics gfx, Size size)
{
	const int startPosition = 0;
	const int samplesPerPixel = 1024;
	var bytesPerSample = (waveStream.WaveFormat.BitsPerSample / 8) * waveStream.WaveFormat.Channels;

	waveStream.Position = 0;
	int bytesRead;
	byte[] waveData = new byte[samplesPerPixel * bytesPerSample];
	waveStream.Position = startPosition + (bytesPerSample * samplesPerPixel);

	for (float x = 0; x < size.Width; x++)
	{
		short low = 0;
		short high = 0;
		bytesRead = waveStream.Read(waveData, 0, samplesPerPixel * bytesPerSample);
		if (bytesRead == 0) break;

		for (int n = 0; n < bytesRead; n += 2)
		{
			short sample = BitConverter.ToInt16(waveData, n);
			if (sample < low) low = sample;
			if (sample > high) high = sample;
		}
		float lowPercent = ((((float)low) - short.MinValue) / ushort.MaxValue);
		float highPercent = ((((float)high) - short.MinValue) / ushort.MaxValue);
		gfx.DrawLine(Pens.Black, x, size.Height * lowPercent, x, size.Height * highPercent);
	}
}

public ISampleProvider CreateSample(
	WaveStream reader,
	float volume,
	int durationSec = 1)
{
	const int bitsPerSecond = 176400;
	var buffer = new byte[bitsPerSecond * durationSec];
	var sample = reader.Read(buffer, 0, bitsPerSecond * durationSec);
	var provider = new RawSourceWaveStream(new MemoryStream(buffer), reader.WaveFormat);
	var sc = new SampleChannel(provider);
	sc.Volume = volume;
	return sc;
}

public class Colours : IDisposable
{
	private Bitmap _scale;
	private int _step = 5;
	private int _i;
	
	public Colours(string scalePath)
	{
		_scale = new Bitmap(scalePath);
	}

	public Color Next()
	{
		_i += _step;
		if (_i > _scale.Height - _step || _i < 1 )
		{
			_step = -_step;
			_i += _step;
		}
		
		return _scale.GetPixel(0, Math.Max(0, Math.Min(_scale.Height - 1, _i)));
	}
	
	public Color Scale(float percent)
	{
		var value = (int) (percent * _scale.Height);
		return _scale.GetPixel(0, Math.Max(0, Math.Min(_scale.Height - 1, value)));
	}

	public void Dispose()
	{
		_scale?.Dispose();
	}
}

public class Wall
{
	public Vector2 Point1 {get; set;}
	public Vector2 Point2 {get; set;}
	public Material Material {get; set;}
	
	public void Draw(Graphics gfx)
	{
		gfx.DrawLine(
			new Pen(Color.LightGray), 
			new Point((int)Point1.X, (int)Point1.Y), new Point((int)Point2.X, (int)Point2.Y));
	}
	
	public bool HasCollided(Particle p)
	{
		var inside1 = PointCollision(Point1, p);
		var inside2 = PointCollision(Point2, p);
		if (inside1 || inside2) return true;

		// get length of the line
		var distX = Point1.X - Point2.X;
		var distY = Point1.Y - Point2.Y;
		var len = (float) Math.Sqrt((distX * distX) + (distY * distY));

		// get dot product of the line and circle
		var dot = (((p.Location.X - Point1.X) * (Point2.X - Point1.X)) + ((p.Location.Y - Point1.Y) * (Point2.Y - Point1.Y))) / (float)Math.Pow(len, 2);

		// find the closest point on the line
		var closestX = Point1.X + (dot * (Point2.X - Point1.X));
		var closestY = Point1.Y + (dot * (Point2.Y - Point1.Y));

		// is this point actually on the line segment? if so keep going, but if not, return false
		var onSegment = LineCollision(Point1, Point2, new Vector2((float)closestX, (float)closestY));
		if (!onSegment) return false;

		// get distance to closest point
		distX = closestX - p.Location.X;
		distY = closestY - p.Location.Y;
		var distance = Math.Sqrt((distX * distX) + (distY * distY));
		
		return distance <= p.Radius;
	}

	private bool PointCollision(Vector2 point, Particle p)
	{
		// get distance between a point and particles center
		// using the Pythagorean Theorem
		var distX = point.X - p.Location.X;
		var distY = point.Y - p.Location.Y;
		var distance = Math.Sqrt((distX * distX) + (distY * distY));

		// if the distance is less than the circle's radius the point is inside
		return distance <= p.Radius;
	}

	private bool LineCollision(Vector2 point1, Vector2 point2, Vector2 p)
	{

		// get distance from the point to the two ends of the line
		var d1 = Vector2.Distance(p, point1);
		var d2 = Vector2.Distance(p, point2);

		// get the length of the line
		var lineLen = Vector2.Distance(point1, point2);

		// since floats are so minutely accurate, add
		// a little buffer zone that will give collision
		var buffer = 0.1;    // higher # = less accurate

		// if the two distances are equal to the line's
		// length, the point is on the line!
		// note we use the buffer here to give a range,
		// rather than one #
		if (d1 + d2 >= lineLen - buffer && d1 + d2 <= lineLen + buffer)
		{
			return true;
		}
		return false;
	}
}

public class Particle
{
	public Color Colour {get; set;}
	public int SoundIndex {get; set;}
	public float Volume {get; set;} // 0-1 as Percentage
	public int Radius { get; set;} = 1;
	public Vector2 Location {get; set;} = Vector2.One;
	public Vector2 Velocity {get; set;} = Vector2.Zero;
	
	public void Draw(Graphics gfx)
	{
		gfx.FillEllipse(new SolidBrush(Colour), Location.X, Location.Y, 2, 2);
	}
	
	public Vector2 Rotate(double degrees)
	{
		const double DegToRad = Math.PI / 180;

		return RotateRadians(degrees * DegToRad);
	}

	public Vector2 RotateRadians(double radians)
	{
		var ca = Math.Cos(radians);
		var sa = Math.Sin(radians);
		return new Vector2(
			(float) (ca * Location.X - sa * Location.Y), 
			(float) (sa * Location.X + ca * Location.Y));
	}
}