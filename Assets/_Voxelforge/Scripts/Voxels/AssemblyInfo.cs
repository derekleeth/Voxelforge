using System.Runtime.CompilerServices;

// Voxel edits are `internal` so only this assembly (the world manager) can change chunks.
// Tests need the same access to verify edit behaviour.
[assembly: InternalsVisibleTo("Voxelforge.Tests.EditMode")]
