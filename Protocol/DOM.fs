
namespace Protocol

[<RequireQualifiedAccess>]
module DOM =

    let [<Literal>] private domain = "DOM."

    type Quad = float []

    type BoxModel =
        {
            content: Quad
            padding: Quad
            border: Quad
            margin: Quad
            width: Integer
            height: Integer
        }        

    type NodeId = Integer

    type Node =
        {
            nodeId: NodeId
            parentId: NodeId option
            backendNodeId: NodeId
            nodeType: Integer
            nodeName: string
            localName: string
            nodeValue: string
            childNodeCount: Integer option
            children: Node list option
            attributes: string list option
            documentURL: string option
        }


    [<RequireQualifiedAccess>]
    module __DescribeNode =
        
        type Parameters =
            {
                nodeId: NodeId
                depth: Integer option
            }

        type Response =
            {            
                node: Node
            }

        type Request =
            ProtocolRequest<Parameters, Response, Node>

    let describeNode (nodeId, depth): __DescribeNode.Request =
        ProtocolRequest (SessionRequired, domain + "describeNode", { nodeId = nodeId; depth = depth }, fun { node = node } -> node)


    let disable: ProtocolRequest<_, unit, _> =
        ProtocolRequest (SessionRequired, domain + "disable", (), id)

    let enable: ProtocolRequest<_, unit, _> =
        ProtocolRequest (SessionRequired, domain + "enable", (), id)


    [<RequireQualifiedAccess>]
    module __GetAttributes =
        
        type Parameters =
            {
                nodeId: NodeId
            }

        type Response =
            {
                attributes: string list
            }

        type Request =
            ProtocolRequest<Parameters, Response, string list>

    let getAttributes nodeId: __GetAttributes.Request =
        ProtocolRequest (SessionRequired, domain + "getAttributes", { nodeId = nodeId }, fun { attributes = attributes } -> attributes)


    [<RequireQualifiedAccess>]
    module __GetBoxModel =
        
        type Parameters =
            {
                nodeId: NodeId
            }

        type Response =
            {
                model: BoxModel
            }

        type Request =
            ProtocolRequest<Parameters, Response, BoxModel>

    let getBoxModel nodeId: __GetBoxModel.Request =
        ProtocolRequest (SessionRequired, domain + "getBoxModel", { nodeId = nodeId }, fun { model = model } -> model)


    [<RequireQualifiedAccess>]
    module __GetDocument =

        type Parameters =
            {
                depth: int option
                flatten: bool
            }

        type Response =
            { root: Node }

        type Request =
            ProtocolRequest<Parameters, Response, Node>

    let getDocument depth: __GetDocument.Request =
        let ``params``: __GetDocument.Parameters =
            { depth = depth; flatten = false }

        ProtocolRequest (SessionRequired, domain + "getDocument", ``params``, function { root = root } -> root)


    [<RequireQualifiedAccess>]
    module __GetOuterHTML =

        type Parameters =
            {
                nodeId: NodeId
            }

        type Response =
            {
                outerHTML: string
            }

        type Request =
            ProtocolRequest<Parameters, Response, string>

    let getOuterHTML nodeId: __GetOuterHTML.Request =
        ProtocolRequest (SessionRequired, domain + "getOuterHTML", { nodeId = nodeId }, fun { outerHTML = outerHTML } -> outerHTML)


    [<RequireQualifiedAccess>]
    module __QuerySelector =
        
        type Parameters =
            {
                nodeId: NodeId
                selector: string
            }

        type Response =
            {
                nodeId: NodeId
            }

        type Request =
            ProtocolRequest<Parameters, Response, NodeId>

    let querySelector nodeId selector: __QuerySelector.Request =
        ProtocolRequest (SessionRequired, domain + "querySelector", { nodeId = nodeId; selector = selector }, fun { nodeId = nodeId } -> nodeId)


    [<RequireQualifiedAccess>]
    module __QuerySelectorAll =
        
        type Parameters =
            {
                nodeId: NodeId
                selector: string
            }

        type Response =
            {
                nodeIds: NodeId list
            }

        type Request =
            ProtocolRequest<Parameters, Response, NodeId list>

    let querySelectorAll nodeId selector: __QuerySelectorAll.Request =
        ProtocolRequest (SessionRequired, domain + "querySelectorAll", { nodeId = nodeId; selector = selector }, fun { nodeIds = nodeIds } -> nodeIds)


    [<RequireQualifiedAccess>]
    module __ScrollIntoViewIfNeeded =
        
        type Parameters =
            {
                nodeId: NodeId
            }

        type Request =
            ProtocolRequest<Parameters, unit, unit>

    let scrollIntoViewIfNeeded nodeId selector: __ScrollIntoViewIfNeeded.Request =
        ProtocolRequest (SessionRequired, domain + "scrollIntoViewIfNeeded", { nodeId = nodeId }, id)


    [<RequireQualifiedAccess>]
    module __SetAttributeValue =

        type Parameters =
            {
                nodeId: NodeId
                name: string
                value: string            
            }

        type Request =
            ProtocolRequest<Parameters, unit, unit>

    let setAttributeValue (nodeId, name, value) =
        let parameters: __SetAttributeValue.Parameters =
            { nodeId = nodeId; name = name; value = value }

        ProtocolRequest (SessionRequired, domain + "setAttributeValue", parameters, id)
