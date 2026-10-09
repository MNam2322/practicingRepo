/** What the slot colours mean. The swatches reuse the slot classes, so the legend always matches the grid. */
export function SlotLegend({ showChosen = true }: { showChosen?: boolean }) {
  return (
    <ul className="legend" aria-label="Legend">
      <li><span className="swatch slot free" aria-hidden="true" />Free</li>
      <li><span className="swatch slot booked" aria-hidden="true" />Booked</li>
      <li><span className="swatch slot past" aria-hidden="true" />Past or too soon</li>
      {showChosen && <li><span className="swatch slot chosen" aria-hidden="true" />Your choice</li>}
    </ul>
  );
}
