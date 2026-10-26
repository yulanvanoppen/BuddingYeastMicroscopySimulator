# Budding Yeast Microscopy Simulator

A Unity simulator of budding yeast colonies growing on a microscope slide, built to generate
**ground-truth training data** for segmentation, tracking and lineage reconstruction.

Current tools segment cells frame by frame, but tracking cells across a movie and recovering who
budded from whom remain hard, and real ground truth for that is scarce. The simulator produces
time-lapse movies in which every pixel's cell identity and every mother–daughter relation is known
exactly.

## What it produces

For each run (folder `Recordings/<date>_seed<seed>/`):

| File | Content |
| --- | --- |
| `labels/labels_0000.png …` | 512×512 RGB label frames. Black = background, one fixed colour per cell for the whole movie. Colours are as contrasting as possible: yellow, blue, red, green, orange, purple, … and each new cell gets the unused colour that stands out most from its neighbours. |
| `labels_id.tif` | 16-bit multi-page TIFF, pixel value = cell id (0 = background). Opens as a time series in Fiji; `tifffile.imread()` gives `(frames, 512, 512)`. |
| `brightfield/brightfield_0000.png …` | Simple simulated brightfield images of the same frames (dark cell wall, bright halo, contact lines, defocus, uneven illumination, camera noise). |
| `labels.avi`, `brightfield.avi` | Lossless FFV1 videos of the frames (requires [ffmpeg](https://ffmpeg.org) on the PATH). |
| `lineage.csv` | One row per cell: `cell_id, mother_id, colony_id, generation, bud_count, color_hex, color_r/g/b, emergence_min, division_min, first_frame, division_frame`. Founders have `mother_id = 0`. |
| `tracks.csv` | One row per visible cell per frame: centroid (`x_px, y_px`), `area_px`, mother, colony and whether it is still an attached bud. |
| `metadata.json` | Seed, pixel size, frame times and every simulation setting used. |

Label images are written losslessly; compressed formats such as MP4 would blend colours at cell edges.
Buds can be labelled separately from the moment they emerge, or merged with their mother until division
(**Bud Labels** on the recorder).

## The model

- **Soft cells.** Each cell is a rigged, slightly elongated yeast mesh. A central core body carries a
  capsule collider; each of the 48 membrane bones is a small rigidbody pulled to its rest position on
  the outline by one spring joint. Neighbouring cells dent each other's outlines and cells can pile up
  slightly under the coverslip. The drawn mesh follows the bones, so what is exported is what collides.
- **Cell cycle.** Buds emerge on the mother (axial, bipolar or random budding pattern), stay attached by
  a neck joint while they grow (volume-linear growth), and separate at a set fraction of adult size.
  Daughters then grow through G1 before budding themselves; mothers rebud after a short G1.
- **Colonies.** Several colonies are seeded in the field of view, each from one or more founder cells.
  Cells rest on the slide (plane spring + floor), are capped by an optional coverslip, are pulled weakly
  toward their colony centre and receive a small random force.
- **Time.** Physics is stepped manually (a fixed number of steps per frame), so results don't depend on
  frame rate and frames are captured exactly at the requested simulated times. 1 unit = 1 µm, time in minutes.

## Running it

1. Open the project with **Unity 6000.6** (URP).
2. First time only: **Yeast Simulator → Clean Up Project…** removes the old prototype (all still in git history),
   then **Yeast Simulator → Create Simulation Scene** builds `Assets/YeastSimulator/Scenes/Simulation.unity`.
3. Press **Play**. The game view shows the colonies (tinted with their label colours); the top-left
   line shows progress. Play Mode stops automatically after the last frame of the last run.

Settings live on the **Simulation** object:

- *SimulationManager*: cell size and shape, membrane stiffness, cell-cycle timing, budding pattern,
  number of colonies, confinement and forces, physics stepping, microscope resolution and pixel size
  (default 512 px × 0.15 µm), label colour assignment.
- *SimulationRecorder*: number of frames, frame interval, warm-up time, number of runs (batch), seed,
  outputs, ffmpeg path and brightfield appearance.

Notes:

- The label capture must not be rescaled: keep **Render Scale = 1** on the URP asset. The recorder warns if
  any decoded label doesn't match a cell.
- More cells means slower physics (≈49 bodies per cell). Speed it up with fewer physics steps per cell
  cycle (*Minutes Per Physics Second*), more *Steps Per Frame*, or a lower *Max Cells*.

## Project layout

```
Assets/YeastSimulator/
├── Scripts/Runtime/   YeastCell (soft body), SimulationManager (cell cycle, colonies, stepping),
│                      SimulationRecorder (export), FrameCapture, BrightfieldRenderer,
│                      LabelPalette, TiffStackWriter, settings
├── Scripts/Editor/    scene builder, one-time project cleanup
├── Shaders/           CellCapture (exact per-pixel cell ids + surface orientation)
├── Models/            budding_yeast.fbx (rigged cell)
└── Scenes/            Simulation.unity
Art/yeast.blend        source of the cell model (Blender)
```
