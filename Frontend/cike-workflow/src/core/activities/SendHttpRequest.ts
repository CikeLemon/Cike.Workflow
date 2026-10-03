import { Activity } from "../abstracts/Activity";
import { CustomHttpHeaders } from "../models/CustomHttpHeaders";
import { Input } from "../models/Input";
import { Output } from "../models/Output";

export const DEFAULT_RESPONSE_ERROR_CODES: number[] = [
  400, 401, 402, 403, 404, 405, 406, 407, 408, 409, 410, 411, 412, 413, 414,
  415, 416, 417, 418, 421, 422, 423, 424, 425, 426, 428, 429, 431, 451, 500,
  501, 502, 503, 504, 505, 506, 507, 508, 510, 511,
];

export class SendHttpRequest extends Activity {
  url: Input<string | null> = new Input<string | null>("Literal", null);
  method: Input<string> = new Input<string>("Literal", "GET");
  content: Input<any> = new Input<any>("Literal", null);
  contentType: Input<string | null> = new Input<string | null>("Literal", null);
  authorization: Input<string | null> = new Input<string | null>(
    "Literal",
    null,
  );
  requestHeaders: Input<CustomHttpHeaders | null> = new Input<
    CustomHttpHeaders | null
  >("Literal", new CustomHttpHeaders());
  timeoutInterval: Input<number> = new Input<number>("Literal", 60);
  responseErrorCodes: Input<number[]> = new Input<number[]>(
    "Literal",
    DEFAULT_RESPONSE_ERROR_CODES,
  );
  waitForCompletion: Input<boolean> = new Input<boolean>("Literal", false);
  suspendOnStatusCodes: Input<number[]> = new Input<number[]>("Literal", [202]);

  statusCode: Output<number> | null = null;
  parsedContent: Output<any> | null = null;
  responseHeaders: Output<CustomHttpHeaders> | null = null;
  callbackPayload: Output<any> | null = null;
}
