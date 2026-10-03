import type { ActivityPort } from "../models/ActivityPort";
import type { Output } from "../models/Output";

export interface IActivity {
  id: string;
  code: string;
  nodeId?: string | null;
  name: string;
  type: string;
  version: number;
  customProperties: Record<string, any>;
  metadata: Record<string, any>;
}

export abstract class Activity implements IActivity {
  id: string;
  code: string;
  nodeId?: string | null = null;
  name: string;
  type: string;
  version: number = 1;
  customProperties: Record<string, any> = {};
  metadata: Record<string, any> = {};

  constructor() {
    this.id = crypto.randomUUID();
    this.code = this.constructor.name;
    this.name = this.constructor.name;
    this.type = `Cike.${this.constructor.name}`;
  }

  /** Entry ports (connection targets). Most activities have a single entry. */
  getInPorts(): ActivityPort[] {
    return [{ name: "In", displayName: "In" }];
  }

  /** Outcome ports (connection sources), e.g. Done, or True/False for If. */
  getOutPorts(): ActivityPort[] {
    return [{ name: "Done", displayName: "Done" }];
  }
}

export abstract class ActivityWithResult<T> extends Activity {
  result: Output<T> | null = null;
}
