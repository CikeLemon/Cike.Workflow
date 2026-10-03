import { MemoryBlockReference } from "./MemoryBlockReference";

export class Output<T> {
    // Phantom marker: keeps the type parameter used without runtime cost.
    declare readonly _typeMarker?: T;
    memoryBlockReference: MemoryBlockReference;

    constructor(id?: string | undefined) {
        this.memoryBlockReference = new MemoryBlockReference(id ?? crypto.randomUUID());
    }
}
