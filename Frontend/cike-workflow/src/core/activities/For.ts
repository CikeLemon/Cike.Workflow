import { Activity } from "../abstracts/Activity";
import { Input } from "../models/Input";
import { Output } from "../models/Output";
import { Flowchart } from "./Flowchart";

export class For extends Activity {
  start: Input<number> = new Input<number>("Literal", 0);
  end: Input<number> = new Input<number>("Literal", 0);
  step: Input<number> = new Input<number>("Literal", 1);
  outerBoundInclusive: Input<boolean> = new Input<boolean>("Literal", true);
  body: Flowchart | null = null;
  currentValue: Output<any> | null = null;
}
