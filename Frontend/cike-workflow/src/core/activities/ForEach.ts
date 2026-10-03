import { Activity } from "../abstracts/Activity";
import { Input } from "../models/Input";
import { Output } from "../models/Output";
import { Flowchart } from "./Flowchart";

export class ForEach extends Activity {
  items: Input<any[]> = new Input<any[]>("Literal", []);
  body: Flowchart | null = null;
  currentValue: Output<any> | null = null;
}
